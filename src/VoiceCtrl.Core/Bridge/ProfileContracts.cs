using VoiceCtrl.Core.Personalization;

namespace VoiceCtrl.Core.Bridge;

// The profiles.* contracts (spec §5.3, research 10 "Profiles"). Only the Hub registers them. As
// with the Dictionary and Snippets, every command that changes the list answers with the list as
// it now is on disk.

public sealed record ProfileList(IReadOnlyList<ProfileEntry> Profiles);

[BridgeCommand("profiles.list", Result = typeof(ProfileList))]
public sealed record ListProfiles;

public sealed record RunningAppList(IReadOnlyList<RunningApp> Apps);

/// <summary>The apps with a window open now, for "Add profile" and "Change app".</summary>
[BridgeCommand("profiles.runningApps", Result = typeof(RunningAppList))]
public sealed record ListRunningApps;

public sealed record ProfileSaveResult(ProfileEditError Error, IReadOnlyList<ProfileEntry> Profiles);

/// <summary>Adds a Profile (<see cref="Original"/> null) or replaces the one for the app
/// <see cref="Original"/>, a process name.</summary>
[BridgeCommand("profiles.save", Result = typeof(ProfileSaveResult))]
public sealed record SaveProfile(string? Original, ProfileEntry Profile);

[BridgeCommand("profiles.remove", Result = typeof(ProfileList))]
public sealed record RemoveProfile(string ProcessName);

/// <summary>Undo for <c>profiles.remove</c>: the entry exactly as the list gave it.</summary>
[BridgeCommand("profiles.restore", Result = typeof(ProfileList))]
public sealed record RestoreProfile(ProfileEntry Profile);
