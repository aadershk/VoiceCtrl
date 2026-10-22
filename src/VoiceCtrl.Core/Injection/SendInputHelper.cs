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

    /// <summary>One press and release of <paramref name="vk"/>, e.g. Press Enter's Enter. Filtered
    /// out of the hotkey hook by <c>LLKHF_INJECTED</c>, like the paste.</summary>
    public static void SendKey(int vk) => Send([KeyDown(vk), KeyUp(vk)]);

    /// <summary>Simulates Ctrl+C, to read the selection (rewrite by voice).</summary>
    public static void SendCtrlC() =>
        Send([KeyDown(NativeMethods.VK_LCONTROL), KeyDown(NativeMethods.VK_C), KeyUp(NativeMethods.VK_C), KeyUp(NativeMethods.VK_LCONTROL)]);

    /// <summary>Shift + Left arrow <paramref name="count"/> times, in one batch so nothing the user
    /// types can land in between: selects that many characters before the caret.</summary>
    public static void SendShiftLeft(int count)
    {
        var inputs = new List<NativeMethods.INPUT>(count * 2 + 2) { KeyDown(NativeMethods.VK_LSHIFT) };
        for (int i = 0; i < count; i++)
        {
            inputs.Add(KeyDown(NativeMethods.VK_LEFT));
            inputs.Add(KeyUp(NativeMethods.VK_LEFT));
        }

        inputs.Add(KeyUp(NativeMethods.VK_LSHIFT));
        Send([.. inputs]);
    }

    private static void Send(NativeMethods.INPUT[] inputs)
    {
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SendInput only accepted {sent}/{inputs.Length} events (GetLastError={error}).");
        }
    }

    // Arrow keys are "extended" keys; without the flag some apps read them as the numpad's.
    private static uint Flags(int vk) => vk is NativeMethods.VK_LEFT or NativeMethods.VK_RIGHT ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0;

    private static NativeMethods.INPUT KeyDown(int vk) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = (ushort)vk, dwFlags = Flags(vk) } },
    };

    private static NativeMethods.INPUT KeyUp(int vk) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT { wVk = (ushort)vk, dwFlags = NativeMethods.KEYEVENTF_KEYUP | Flags(vk) },
        },
    };
}
