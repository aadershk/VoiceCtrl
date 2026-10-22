using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Bridge;

// The style.* contracts (ticket 56): the Style screen. Only the Hub registers them. Every command
// answers with the screen as it now is, so the page never keeps its own copy in step by hand.

/// <summary>An app on a group: <see cref="Id"/> is what <c>style.moveApp</c> takes back (a listed
/// app's name, or an unlisted app's process name); <see cref="Moved"/> marks one the user moved here.</summary>
public sealed record StyleApp(string Id, string Name, bool Moved);

/// <summary>One example sentence as <see cref="Style"/> writes it; null for AI prompts and code.</summary>
public sealed record StyleExample(Style? Style, string Text);

/// <summary>A kind of app: its Style (null for AI prompts and code), its apps and its examples.</summary>
public sealed record StyleGroup(AppKind Kind, Style? Style, IReadOnlyList<StyleApp> Apps, IReadOnlyList<StyleExample> Examples);

public sealed record StyleView(IReadOnlyList<StyleGroup> Groups, string YourName);

[BridgeCommand("style.get", Result = typeof(StyleView))]
public sealed record GetStyle;

[BridgeCommand("style.set", Result = typeof(StyleView))]
public sealed record SetStyle(AppKind Kind, Style Style);

/// <summary>"Your name", for the sign-off of an email VoiceCtrl writes (ticket 57).</summary>
[BridgeCommand("style.setName", Result = typeof(StyleView))]
public sealed record SetStyleName(string Name);

/// <summary>Moves an app to another kind; <see cref="App"/> is a <see cref="StyleApp.Id"/> or, to
/// add an app VoiceCtrl doesn't list, its process name.</summary>
[BridgeCommand("style.moveApp", Result = typeof(StyleView))]
public sealed record MoveStyleApp(string App, AppKind Kind);
