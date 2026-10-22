using System.IO;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// Opens the settings and the secrets at startup (spec §2, step 3), running the one-time v3
/// migration first when settings.json doesn't exist yet. The key is saved before settings.json is
/// written, so a crash in between just runs the migration again on the next start.
/// </summary>
public static class SettingsStartup
{
    public static (SettingsStore Settings, SecretsStore Secrets) Open() =>
        Open(UserDataPaths.Root, V3Breadcrumbs.FromThisMachine);

    /// <param name="readBreadcrumbs">Only called when the migration runs, so a normal start never
    /// touches the registry or the Start Menu.</param>
    internal static (SettingsStore Settings, SecretsStore Secrets) Open(string dataRoot, Func<V3Breadcrumbs> readBreadcrumbs)
    {
        SecretsStore secrets = SecretsStore.Load(Path.Combine(dataRoot, UserDataPaths.SecretsFileName));
        SettingsStore settings = SettingsStore.Open(
            Path.Combine(dataRoot, UserDataPaths.SettingsFileName),
            () => MigrateFromV3(dataRoot, readBreadcrumbs(), secrets));

        // Ticket 60: a Gemini key keeps Gemini; everyone else starts on NVIDIA.
        Online.ProviderMigration.Apply(settings, secrets);
        return (settings, secrets);
    }

    private static UserSettings MigrateFromV3(string dataRoot, V3Breadcrumbs breadcrumbs, SecretsStore secrets)
    {
        V3MigrationResult v3 = V3Migration.Read(dataRoot, breadcrumbs);

        // A key already in secrets.bin was saved by v4, so it's newer than any v3 .env.
        bool keyMigrated = v3.GeminiApiKey is not null && secrets.GeminiApiKey.Length == 0;
        if (keyMigrated)
        {
            secrets.SetGeminiApiKey(v3.GeminiApiKey!);
        }

        SimpleFileLogger.LogInfo(v3.FoundV3
            ? $"v3 migration: .env {(v3.EnvFilePath is null ? "not found" : $"found at {v3.EnvFilePath}")}, key {(keyMigrated ? "carried over" : "not carried over")}, Setup skipped"
            : "v3 migration: no v3 install found, fresh settings");

        return v3.Settings;
    }
}
