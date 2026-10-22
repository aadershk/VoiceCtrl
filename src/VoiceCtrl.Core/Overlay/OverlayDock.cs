namespace VoiceCtrl.Core.Overlay;

/// <summary>Where the Overlay sits on the active monitor (DESIGN §9). The bar stays horizontal on
/// every dock and grows away from the screen edge.</summary>
public enum OverlayDock
{
    /// <summary>Centred, 12 px above the taskbar; grows from its centre. The default.</summary>
    Bottom,

    /// <summary>16 px from the left edge, centred vertically; grows rightwards.</summary>
    Left,

    /// <summary>16 px from the right edge, centred vertically; grows leftwards.</summary>
    Right,
}
