using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Online;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// The Online providers' keys (one per provider, ticket 60) and the TypeSafe key, in %LocalAppData%\VoiceCtrl\secrets.bin, encrypted with DPAPI for
/// the current Windows user (spec §5.2). Kept apart from settings.json so that file can be shown,
/// copied into diagnostics or hand-edited without ever exposing a key. Nothing here logs a key.
/// </summary>
public sealed class SecretsStore
{
    // Not a secret (it ships in the binary). It scopes the blob to this app, so a generic
    // "unprotect everything for this user" call without it gets nothing.
    private static readonly byte[] Entropy = "VoiceCtrl.secrets.v1"u8.ToArray();

    /// <summary>Null when this run can't safely write the file, same rule as SettingsStore.</summary>
    private readonly string? _filePath;
    private readonly object _gate = new();
    private readonly Dictionary<OnlineProvider, string> _providerKeys = [];
    private string _typeSafeApiKey;

    private SecretsStore(string? filePath, SecretsFile? file = null)
    {
        _filePath = filePath;
        _typeSafeApiKey = file?.TypeSafeApiKey ?? string.Empty;
        foreach (OnlineProvider provider in OnlinePresets.FallbackOrder)
        {
            _providerKeys[provider] = (file is null ? null : FieldOf(file, provider)) ?? string.Empty;
        }
    }

    /// <summary>The Gemini key; empty when not set.</summary>
    public string GeminiApiKey => KeyFor(OnlineProvider.Gemini);

    /// <summary>The key saved for <paramref name="provider"/>; empty when not set.</summary>
    public string KeyFor(OnlineProvider provider)
    {
        lock (_gate)
        {
            return _providerKeys.GetValueOrDefault(provider, string.Empty);
        }
    }

    /// <summary>Every key saved, of any kind, for scrubbing them out of a log.</summary>
    public IReadOnlyList<string> AllKeys
    {
        get
        {
            lock (_gate)
            {
                return [.. _providerKeys.Values.Where(key => key.Length > 0), .. _typeSafeApiKey.Length > 0 ? [_typeSafeApiKey] : Array.Empty<string>()];
            }
        }
    }

    /// <summary>Empty when not set.</summary>
    public string TypeSafeApiKey
    {
        get
        {
            lock (_gate)
            {
                return _typeSafeApiKey;
            }
        }
    }

    /// <summary>Raised after a key actually changed, on the calling thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// Reads and decrypts <paramref name="filePath"/>. A missing file means no keys yet. A file that
    /// won't decrypt (another user's or another PC's, or damaged) is set aside and the keys start
    /// empty, so the user is asked for them again rather than the app failing to start.
    /// </summary>
    public static SecretsStore Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new SecretsStore(filePath);
        }

        byte[] protectedBytes;
        try
        {
            protectedBytes = File.ReadAllBytes(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogError("secrets.bin could not be read; keys unavailable for this run", ex);
            return new SecretsStore(filePath: null);
        }

        try
        {
            byte[] json = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            SecretsFile file = JsonSerializer.Deserialize<SecretsFile>(json, SettingsStore.JsonOptions) ?? new SecretsFile();
            return new SecretsStore(filePath, file);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // The exception's message never contains the plaintext; only its type is logged anyway.
            string? asidePath = AtomicFile.MoveAside(filePath, "unreadable");
            SimpleFileLogger.LogInfo($"secrets.bin could not be decrypted ({ex.GetType().Name}); kept it as {Path.GetFileName(asidePath)} and cleared the keys");
            return new SecretsStore(asidePath is null ? null : filePath);
        }
    }

    /// <summary>A store with no backing file, for tests.</summary>
    internal static SecretsStore InMemory() => new(filePath: null);

    /// <summary>Saves the Gemini key, trimmed. Empty clears it.</summary>
    public void SetGeminiApiKey(string key) => SetKey(OnlineProvider.Gemini, key);

    /// <summary>Saves <paramref name="provider"/>'s key, trimmed. Empty clears it.</summary>
    public void SetKey(OnlineProvider provider, string key)
    {
        bool changed;
        lock (_gate)
        {
            string value = key.Trim();
            changed = _providerKeys.GetValueOrDefault(provider, string.Empty) != value;
            if (changed)
            {
                _providerKeys[provider] = value;
                Persist();
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Saves the TypeSafe key, trimmed. Empty clears it.</summary>
    public void SetTypeSafeApiKey(string key)
    {
        bool changed;
        lock (_gate)
        {
            changed = SetAndPersist(ref _typeSafeApiKey, key.Trim());
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    // Callers hold _gate, so the file always gets both keys as they stand together.
    private bool SetAndPersist(ref string field, string value)
    {
        if (field == value)
        {
            return false;
        }

        field = value;
        Persist();
        return true;
    }

    private void Persist()
    {
        if (_filePath is null)
        {
            return;
        }

        try
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                new SecretsFile
                {
                    GeminiApiKey = _providerKeys[OnlineProvider.Gemini],
                    NvidiaApiKey = _providerKeys[OnlineProvider.Nvidia],
                    OpenAiApiKey = _providerKeys[OnlineProvider.OpenAi],
                    OtherApiKey = _providerKeys[OnlineProvider.Other],
                    TypeSafeApiKey = _typeSafeApiKey,
                },
                SettingsStore.JsonOptions);
            AtomicFile.WriteAllBytes(_filePath, ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            SimpleFileLogger.LogError("Could not save secrets.bin", ex);
        }
    }

    private static string? FieldOf(SecretsFile file, OnlineProvider provider) => provider switch
    {
        OnlineProvider.Gemini => file.GeminiApiKey,
        OnlineProvider.Nvidia => file.NvidiaApiKey,
        OnlineProvider.OpenAi => file.OpenAiApiKey,
        _ => file.OtherApiKey,
    };

    // A class, not a record: a record's generated ToString would print the keys. The field names
    // stay as v1 wrote them, so a file from before ticket 60 still reads; the new keys are optional.
    private sealed class SecretsFile
    {
        public int Version { get; set; } = 1;

        public string? GeminiApiKey { get; set; }

        public string? NvidiaApiKey { get; set; }

        public string? OpenAiApiKey { get; set; }

        public string? OtherApiKey { get; set; }

        public string? TypeSafeApiKey { get; set; }
    }
}
