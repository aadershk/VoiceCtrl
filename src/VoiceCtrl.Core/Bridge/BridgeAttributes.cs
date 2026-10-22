namespace VoiceCtrl.Core.Bridge;

/// <summary>
/// Marks a record as the arguments of a bridge command (spec §4). The record's properties are the
/// command's <c>args</c>; <see cref="Result"/> is what the host answers with (null means the
/// command answers <c>null</c>). <c>tools/BridgeTypegen</c> reads these to write
/// <c>ui/src/bridge/contracts.g.ts</c>, so this attribute is the single source of truth for both
/// sides.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BridgeCommandAttribute(string name) : Attribute
{
    /// <summary>The wire name, <c>namespace.verb</c>, e.g. <c>app.setTheme</c>.</summary>
    public string Name { get; } = name;

    /// <summary>The type the host answers with, or null for a command that answers null.</summary>
    public Type? Result { get; init; }
}

/// <summary>Marks a record as the payload of a host → page event (spec §4).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BridgeEventAttribute(string name) : Attribute
{
    /// <summary>The wire name, <c>namespace.noun</c>, e.g. <c>app.theme</c>.</summary>
    public string Name { get; } = name;
}
