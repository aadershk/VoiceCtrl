# One .NET host, every surface in WebView2 (supersedes 0001)

ADR-0001 kept the Hub and Overlay in native WPF because the owners' constraint then was that
the app must not get heavier or slower at idle. For v4.0 the owners reversed that: looks come
first, and a heavier app is acceptable if it buys top-tier visuals (standing decision,
2026-09-24; confirmed by Vedarth on the UI technology decision ticket). So every VoiceCtrl
surface (Hub, Setup, Overlay, tray flyout) is now HTML/CSS/TypeScript rendered by WebView2
inside the existing .NET 8 host, and VoiceCtrl.Core, the keyboard hook, paste injection, audio
and the tray icon stay C# in-process. We chose this over WinUI 3 and Avalonia (both mean a
full XAML rewrite that can't reuse the HTML design mockups, plus weaker transparent-overlay
support), and over Electron and Tauri (the same web UI, but Core would have to move behind a
sidecar and IPC boundary). Measured cost on a hello-world: about 280 MB private RAM against 120
MB for WPF, with the same warm start time (about 0.6 s). The Overlay is gated by a spike (show
latency at most 50 ms, no focus change, 60 fps, click-through while hidden); if it fails, the
Overlay alone stays native WPF. Evidence: .scratch/v4-overhaul/research/05-ui-technology-options.md.

**Addendum (2026-09-29, committed with the WebView2 host):** the Overlay spike passed (VED-20),
so the WPF fallback is not used. Its three construction rules are part of this decision: the
Overlay's host window is created with `WS_EX_NOREDIRECTIONBITMAP`; it is always click-through
(`WS_EX_TRANSPARENT | WS_EX_LAYERED`), with clicks, hover and drag forwarded from a `WH_MOUSE_LL`
hook on its own thread; and its morphs animate `transform` and `opacity` only. The host is
WinForms rather than WPF, because the Overlay needs a raw Win32 window, which WPF can't host,
and every other surface is just a window around a WebView2. This also supersedes ADR-0002: the
windows are frameless, and each page draws its own title bar.
