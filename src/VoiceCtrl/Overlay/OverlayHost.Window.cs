using System.Diagnostics;
using System.Windows.Forms;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Overlay;

// The Overlay's raw Win32 window, and the sandbox-only stand-ins for real input.
internal sealed partial class OverlayHost
{
#if DEBUG
    /// <summary>
    /// Sandbox-only stand-ins for real input, for the agent's checks (memory "UI audit sandbox
    /// harness"): the owner's mouse and keyboard are never driven. A posted toggle is the Hotkey
    /// double-tap as the input thread delivers it; a posted pointer event (lParam = x | y &lt;&lt; 16,
    /// screen px) takes the mouse hook's path from the point it posts to the UI thread; a posted
    /// Esc is the keyboard hook's Esc.
    /// </summary>
    private void OnDebugMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        if (UserDataPaths.SandboxRoot is null)
        {
            return;
        }

        _debugGesture = Stopwatch.GetTimestamp();
        if (message == OverlayWindow.DebugToggleMessage)
        {
            _ = _controller.ToggleBar();
        }
        else if (message == OverlayWindow.DebugEscapeMessage)
        {
            if (!CancelDrag())
            {
                _controller.Cancel();
            }
        }
        else
        {
            var kind = (OverlayPointerKind)(int)wParam;
            long packed = lParam.ToInt64();
            OnPointer(kind, (short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));
        }
    }

    private long _debugGesture;
#endif

    /// <summary>The raw popup: layered, transparent to input, never activated, no taskbar button,
    /// and no redirection bitmap, so WebView2's DirectComposition output is the only pixels.</summary>
    private sealed class OverlayWindow : NativeWindow
    {
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int WM_DPICHANGED = 0x02E0;
        private const int MA_NOACTIVATE = 3;

#if DEBUG
        public const int DebugToggleMessage = (int)Win32.WM_APP + 0x40;
        public const int DebugPointerMessage = (int)Win32.WM_APP + 0x41;
        public const int DebugEscapeMessage = (int)Win32.WM_APP + 0x42;
#endif

        private readonly OverlayHost _owner;

        public OverlayWindow(OverlayHost owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams
            {
                Caption = "VoiceCtrl Overlay",
                Style = WS_POPUP,
                ExStyle = WS_EX_NOREDIRECTIONBITMAP | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
                    | WS_EX_TRANSPARENT | WS_EX_LAYERED,
            });
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                // Belt and braces: the window takes no input, but never activate on a click anyway.
                case WM_MOUSEACTIVATE:
                    m.Result = MA_NOACTIVATE;
                    return;

                // The host sizes the window for each monitor itself (OverlayHost.Place).
                case WM_DPICHANGED:
                    m.Result = IntPtr.Zero;
                    return;

#if DEBUG
                case DebugToggleMessage:
                case DebugPointerMessage:
                case DebugEscapeMessage:
                    _owner.OnDebugMessage(m.Msg, m.WParam, m.LParam);
                    return;
#endif
            }

            base.WndProc(ref m);
        }
    }
}
