using System.Runtime.InteropServices;
using VoiceCtrl.Core.Interop;

namespace VoiceCtrl.Core.Injection;

public static class SendInputHelper
{
    /// <summary>
    /// Simulates Ctrl+V. Both Left and Right Ctrl are tracked hotkey keys, so the VK choice here
    /// no longer distinguishes this from a real press, so <c>LLKHF_INJECTED</c> filtering in
    /// <see cref="Hotkey.LowLevelKeyboardHook"/> is the sole safeguard preventing this synthesized
    /// paste from self-triggering the hotkey. Do not remove that filter.
    /// </summary>
    public static void SendCtrlV()
    {
        int inputSize = Marshal.SizeOf<NativeMethods.INPUT>();
        NativeMethods.INPUT[] inputs =
        [
            KeyDown(NativeMethods.VK_LCONTROL),
            KeyDown(NativeMethods.VK_V),
            KeyUp(NativeMethods.VK_V),
            KeyUp(NativeMethods.VK_LCONTROL),
        ];

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, inputSize);
        if (sent != inputs.Length)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SendInput only accepted {sent}/{inputs.Length} events (GetLastError={error}).");
        }
    }

    private static NativeMethods.INPUT KeyDown(int vk) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = (ushort)vk } },
    };

    private static NativeMethods.INPUT KeyUp(int vk) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT { wVk = (ushort)vk, dwFlags = NativeMethods.KEYEVENTF_KEYUP },
        },
    };
}
