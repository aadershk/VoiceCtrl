using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Setup;

namespace VoiceCtrl.Core.Bridge;

// The setup.* contracts (spec §7 Setup). Only the Setup window registers them.

/// <summary>What the Setup page needs beyond the <c>app.ready</c> snapshot.</summary>
/// <param name="Hotkey">The Hotkey's name as the key chips show it, e.g. "Ctrl".</param>
public sealed record SetupSnapshot(string Hotkey, bool StartWithWindows, ModelDownloadStatus Model);

[BridgeCommand("setup.state", Result = typeof(SetupSnapshot))]
public sealed record GetSetupState;

public sealed record KeyCheckResult(SetupKeyCheck Check);

/// <summary>Checks a provider's key against its real service (ticket 60). The page can't: its CSP
/// allows no network. <see cref="BaseUrl"/> and <see cref="Model"/> are Other's, ignored otherwise.</summary>
[BridgeCommand("setup.checkKey", Result = typeof(KeyCheckResult))]
public sealed record CheckKey(string Key, OnlineProvider Provider, string? BaseUrl = null, string? Model = null);

/// <summary>Starts or resumes the English model download; progress arrives as <c>setup.model</c>.</summary>
[BridgeCommand("setup.downloadModel")]
public sealed record DownloadModel;

/// <summary>Stops the download quietly (Back or Skip). What is on disk stays for a resume.</summary>
[BridgeCommand("setup.stopDownload")]
public sealed record StopModelDownload;

/// <summary>Open VoiceCtrl: saves the choices, applies Start with Windows, closes Setup and opens
/// the Hub on Dictations. <see cref="ApiKey"/> is null when the key was skipped; <see cref="BaseUrl"/>
/// and <see cref="Model"/> are Other's.</summary>
[BridgeCommand("setup.finish")]
public sealed record FinishSetup(
    SetupListening Listening, string? ApiKey, bool StartWithWindows, bool SmartCleanup,
    OnlineProvider Provider, string? BaseUrl = null, string? Model = null);

[BridgeEvent("setup.model")]
public sealed record ModelDownloadChanged(ModelDownloadStatus Status);
