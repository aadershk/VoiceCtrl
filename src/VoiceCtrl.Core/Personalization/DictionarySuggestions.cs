using System.IO;
using System.Text;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Setup;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Personalization;

/// <param name="Count">How many kept Dictations it was said in: the chip's "6×".</param>
public sealed record DictionarySuggestion(string Word, int Count);

/// <summary>The Dictionary screen's Suggestions card.</summary>
/// <param name="On">"Suggest Dictionary words"; off shows the dashed Turn on card.</param>
/// <param name="Offline">Mode is Offline, so none are made at all.</param>
public sealed record SuggestionsView(bool On, bool Offline, IReadOnlyList<DictionarySuggestion> Items);

/// <summary>Settings' Word suggestions group. The key itself never leaves the host.</summary>
public sealed record SuggestionSettingsView(bool On, ApiKeyState KeyState, string? KeyMask);

/// <summary>
/// Dictionary Suggestions (CONTEXT; spec §5.3; research 06, candidate c): words found in the kept
/// Dictations, offered on the Dictionary screen and never added without the user's yes.
/// <list type="bullet">
/// <item>Off by default. Offline Mode makes none and never contacts TypeSafe, not even to check a
/// key: choosing Offline means nothing leaves the PC.</item>
/// <item><see cref="SuggestionMiner"/> finds candidates in code. With the user's own TypeSafe key,
/// every candidate said in two Dictations or more is judged in one request and shown when TypeSafe
/// gives it 0.7 or more. Without a key, or when TypeSafe can't answer, code alone offers what was
/// said in three Dictations or more.</item>
/// <item>A judged word is not sent again this run. Ignored words are kept on disk, so they never
/// come back.</item>
/// </list>
/// The TypeSafe key follows the Gemini key's rules (<see cref="TranscriptionSettingsService"/>):
/// checked before it is saved, a refused key never replaces the saved one, and one that couldn't be
/// checked is saved anyway.
/// </summary>
public sealed class DictionarySuggestions : IDisposable
{
    /// <summary>TypeSafe's probability a word must reach to be offered. From research 06, to be
    /// tuned on real Add and Ignore rates.</summary>
    public const double Threshold = 0.7;

    public const int MinDictationsJudged = 2;

    public const int MinDictationsCodeOnly = 3;

    public const int MaxShown = 6;

    /// <summary>Keeps one request small: the most recurring candidates only.</summary>
    public const int MaxJudged = 40;

    private const int MaxIgnored = 500;
    private const int MaxKeyLength = 200;

    private readonly SettingsStore _settings;
    private readonly SecretsStore _secrets;
    private readonly PersonalizationLibrary _library;
    private readonly Func<IReadOnlyList<string>> _dictations;
    private readonly ISuggestionJudge _judge;
    private readonly string _ignoredPath;
    private readonly object _gate = new();
    private readonly HashSet<string> _ignored;
    private readonly Dictionary<string, double> _judged = new(StringComparer.OrdinalIgnoreCase);
    private string _judgedWithKey = "";
    private string? _verifiedKey;
    private string? _refusedKey;
    private string? _checkedKey;

    public DictionarySuggestions(
        SettingsStore settings,
        SecretsStore secrets,
        PersonalizationLibrary library,
        Func<IReadOnlyList<string>> dictations,
        ISuggestionJudge judge,
        string ignoredPath)
    {
        _settings = settings;
        _secrets = secrets;
        _library = library;
        _dictations = dictations;
        _judge = judge;
        _ignoredPath = ignoredPath;
        _ignored = ReadIgnored(ignoredPath);

        _settings.PrivacyChanged += OnPrivacyChanged;
        _secrets.Changed += RaiseChanged;
    }

    /// <summary>Something the Word suggestions rows show changed. Raised on whatever thread caused it.</summary>
    public event Action? Changed;

    private bool IsOffline => _settings.Current.Transcription.Mode == TranscriptionModePreference.Offline;

    public SuggestionSettingsView SettingsView()
    {
        string key = _secrets.TypeSafeApiKey;
        return new SuggestionSettingsView(
            _settings.Current.Privacy.SuggestDictionaryWords,
            KeyStateOf(key),
            key.Length == 0 ? null : TranscriptionSettingsService.Mask(key));
    }

    public void SetOn(bool on) =>
        _settings.Update(s => s with { Privacy = s.Privacy with { SuggestDictionaryWords = on } });

    /// <summary>The Suggestions card, freshly mined. May call TypeSafe once, for words it hasn't judged.</summary>
    public async Task<SuggestionsView> GetAsync(CancellationToken cancellationToken = default)
    {
        bool on = _settings.Current.Privacy.SuggestDictionaryWords;
        bool offline = IsOffline;
        if (!on || offline)
        {
            return new SuggestionsView(on, offline, []);
        }

        IReadOnlyList<SuggestionCandidate> candidates = SuggestionMiner.Find(_dictations(), Excluded());
        IReadOnlyList<DictionarySuggestion>? judged = await JudgedAsync(candidates, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DictionarySuggestion> items = judged ?? candidates
            .Where(c => c.Dictations >= MinDictationsCodeOnly)
            .Take(MaxShown)
            .Select(c => new DictionarySuggestion(c.Word, c.Dictations))
            .ToList();

        return new SuggestionsView(true, false, items);
    }

    /// <summary>The Dictionary as it now is on disk, for the page after an Add.</summary>
    public IReadOnlyList<DictionaryEntry> Dictionary() => _library.Dictionary();

    /// <summary>The chip's Add: the word goes on top of the Dictionary, as Add word would put it.</summary>
    public DictionaryEditError Add(string word) => _library.SaveWord(null, word, string.Empty);

    /// <summary>The chip's Ignore: it is never offered again.</summary>
    public void Ignore(string word)
    {
        string trimmed = word.Trim();
        if (trimmed.Length == 0 || trimmed.Length > CustomDictionary.MaxTermLength)
        {
            return;
        }

        string[] lines;
        lock (_gate)
        {
            if (_ignored.Count >= MaxIgnored || !_ignored.Add(trimmed))
            {
                return;
            }

            lines = [.. _ignored];
        }

        try
        {
            AtomicFile.WriteAllBytes(_ignoredPath, Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still ignored for this run; it may come back after a restart.
            SimpleFileLogger.LogInfo($"Couldn't save the ignored suggestions: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks <paramref name="key"/> with TypeSafe, then saves it unless TypeSafe refused it. In
    /// Offline Mode it is saved unchecked, since Offline never contacts TypeSafe.
    /// </summary>
    public async Task<SetupKeyCheck> SaveKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        key = key?.Trim() ?? "";
        if (key.Length is 0 or > MaxKeyLength)
        {
            throw new ArgumentException("The key is empty or too long.", nameof(key));
        }

        TypeSafeOutcome outcome = IsOffline
            ? TypeSafeOutcome.Unavailable
            : await _judge.CheckKeyAsync(key, cancellationToken).ConfigureAwait(false);
        if (outcome == TypeSafeOutcome.KeyRefused)
        {
            return SetupKeyCheck.DidNotWork;
        }

        lock (_gate)
        {
            _verifiedKey = outcome == TypeSafeOutcome.Answered ? key : null;
            _checkedKey = _verifiedKey;
            _refusedKey = null;
        }

        _secrets.SetTypeSafeApiKey(key);
        RaiseChanged();
        return outcome == TypeSafeOutcome.Answered ? SetupKeyCheck.Works : SetupKeyCheck.NotChecked;
    }

    public void RemoveKey() => _secrets.SetTypeSafeApiKey("");

    /// <summary>Checks a key from an earlier run once, when Settings first shows it, so the row can
    /// say "Works" or that it was refused. Never in Offline Mode.</summary>
    public async Task VerifySavedKeyAsync(CancellationToken cancellationToken = default)
    {
        string key = _secrets.TypeSafeApiKey;
        lock (_gate)
        {
            if (key.Length == 0 || _checkedKey == key || IsOffline)
            {
                return;
            }

            _checkedKey = key;
        }

        TypeSafeOutcome outcome = await _judge.CheckKeyAsync(key, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_secrets.TypeSafeApiKey != key)
            {
                return;
            }

            switch (outcome)
            {
                case TypeSafeOutcome.Answered:
                    _verifiedKey = key;
                    break;
                case TypeSafeOutcome.KeyRefused:
                    _refusedKey = key;
                    break;
                default:
                    // Could not be checked; forgotten so the next Settings visit tries again.
                    _checkedKey = null;
                    return;
            }
        }

        RaiseChanged();
    }

    /// <summary>The words TypeSafe ranks highest, or null when code alone must decide: no key, a
    /// refused key, or TypeSafe not answering.</summary>
    private async Task<IReadOnlyList<DictionarySuggestion>?> JudgedAsync(IReadOnlyList<SuggestionCandidate> candidates, CancellationToken cancellationToken)
    {
        string key = _secrets.TypeSafeApiKey;
        List<SuggestionCandidate> pool = [.. candidates.Where(c => c.Dictations >= MinDictationsJudged).Take(MaxJudged)];
        List<SuggestionCandidate> unjudged;
        lock (_gate)
        {
            if (key.Length == 0 || _refusedKey == key)
            {
                return null;
            }

            // Another key may judge differently; its answers start afresh.
            if (_judgedWithKey != key)
            {
                _judged.Clear();
                _judgedWithKey = key;
            }

            unjudged = [.. pool.Where(c => !_judged.ContainsKey(c.Word))];
        }

        if (unjudged.Count > 0)
        {
            TypeSafeJudgement judgement = await _judge.JudgeAsync(key, unjudged, cancellationToken).ConfigureAwait(false);
            if (judgement.Outcome == TypeSafeOutcome.KeyRefused)
            {
                lock (_gate)
                {
                    _refusedKey = key;
                    _verifiedKey = null;
                }

                RaiseChanged();
                return null;
            }

            if (judgement.Outcome != TypeSafeOutcome.Answered)
            {
                return null;
            }

            lock (_gate)
            {
                if (_judgedWithKey != key)
                {
                    return null;
                }

                foreach ((string word, double noul) in judgement.Nouls)
                {
                    _judged[word] = noul;
                }

                _verifiedKey = key;
            }
        }

        lock (_gate)
        {
            return pool
                .Where(c => _judged.TryGetValue(c.Word, out double noul) && noul >= Threshold)
                .OrderByDescending(c => _judged[c.Word] * c.Dictations)
                .Take(MaxShown)
                .Select(c => new DictionarySuggestion(c.Word, c.Dictations))
                .ToList();
        }
    }

    /// <summary>Everything that must never be offered: the Dictionary's words and their "also
    /// heard as" phrases, and what was ignored.</summary>
    private List<string> Excluded()
    {
        List<string> excluded = [.. _library.Dictionary().SelectMany(e => e.AlsoHeardAs.Prepend(e.Word))];
        lock (_gate)
        {
            excluded.AddRange(_ignored);
        }

        return excluded;
    }

    private ApiKeyState KeyStateOf(string key)
    {
        if (key.Length == 0)
        {
            return ApiKeyState.None;
        }

        lock (_gate)
        {
            return _refusedKey == key ? ApiKeyState.Rejected
                : _verifiedKey == key ? ApiKeyState.Works
                : ApiKeyState.Saved;
        }
    }

    private static HashSet<string> ReadIgnored(string path)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(path))
            {
                foreach (string line in File.ReadLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).Take(MaxIgnored))
                {
                    ignored.Add(line);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogInfo($"Couldn't read the ignored suggestions: {ex.Message}");
        }

        return ignored;
    }

    private void OnPrivacyChanged(PrivacySection section) => RaiseChanged();

    private void RaiseChanged() => Changed?.Invoke();

    public void Dispose()
    {
        _settings.PrivacyChanged -= OnPrivacyChanged;
        _secrets.Changed -= RaiseChanged;
    }
}
