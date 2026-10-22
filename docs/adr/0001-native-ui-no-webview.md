# Native WPF UI for the Hub and Overlay, no Electron/webview

**Superseded by [ADR-0003](0003-webview2-ui-supersedes-0001.md) (2026-09-29).** Kept for the history of the decision; v4 renders every surface in WebView2.

The Hub and redesigned Overlay could have been built as a browser-based UI (Electron or a WebView2 host), which is common for polished-looking desktop apps and would make Wispr-Flow-style visuals faster to iterate on. We chose to keep them native WPF/XAML, matching the rest of VoiceCtrl, because the user's explicit constraint was that the app must not get heavier or slower at idle — a webview runtime carries meaningfully more memory and startup cost than native windows, and VoiceCtrl's whole value proposition is staying out of the way. The Hub and its editors are also lazy-loaded (built only when opened from the tray) for the same reason.
