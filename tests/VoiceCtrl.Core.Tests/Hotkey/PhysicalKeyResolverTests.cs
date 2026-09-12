using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Interop;
using Xunit;

namespace VoiceCtrl.Core.Tests.Hotkey;

public class PhysicalKeyResolverTests
{
    [Fact]
    public void ExactLeftControlVk_ResolvesToLeftControl()
    {
        int result = PhysicalKeyResolver.Normalize((uint)NativeMethods.VK_LCONTROL, flags: 0);

        Assert.Equal(NativeMethods.VK_LCONTROL, result);
    }

    [Fact]
    public void ExactRightControlVk_ResolvesToRightControl()
    {
        int result = PhysicalKeyResolver.Normalize((uint)NativeMethods.VK_RCONTROL, flags: 0);

        Assert.Equal(NativeMethods.VK_RCONTROL, result);
    }

    [Fact]
    public void GenericControlVkWithExtendedFlag_ResolvesToRightControl()
    {
        int result = PhysicalKeyResolver.Normalize((uint)NativeMethods.VK_CONTROL, NativeMethods.LLKHF_EXTENDED);

        Assert.Equal(NativeMethods.VK_RCONTROL, result);
    }

    [Fact]
    public void GenericControlVkWithoutExtendedFlag_ResolvesToLeftControl()
    {
        int result = PhysicalKeyResolver.Normalize((uint)NativeMethods.VK_CONTROL, flags: 0);

        Assert.Equal(NativeMethods.VK_LCONTROL, result);
    }

    [Fact]
    public void GenericShiftVkWithExtendedFlag_ResolvesToRightShift()
    {
        int result = PhysicalKeyResolver.Normalize((uint)NativeMethods.VK_SHIFT, NativeMethods.LLKHF_EXTENDED);

        Assert.Equal(NativeMethods.VK_RSHIFT, result);
    }

    [Fact]
    public void GenericShiftVkWithoutExtendedFlag_ResolvesToLeftShift()
    {
        int result = PhysicalKeyResolver.Normalize((uint)NativeMethods.VK_SHIFT, flags: 0);

        Assert.Equal(NativeMethods.VK_LSHIFT, result);
    }

    [Fact]
    public void GenericMenuVkWithExtendedFlag_ResolvesToRightAlt()
    {
        int result = PhysicalKeyResolver.Normalize((uint)NativeMethods.VK_MENU, NativeMethods.LLKHF_EXTENDED);

        Assert.Equal(NativeMethods.VK_RMENU, result);
    }

    [Fact]
    public void GenericMenuVkWithoutExtendedFlag_ResolvesToLeftAlt()
    {
        int result = PhysicalKeyResolver.Normalize((uint)NativeMethods.VK_MENU, flags: 0);

        Assert.Equal(NativeMethods.VK_LMENU, result);
    }

    [Fact]
    public void UnrelatedVk_PassesThroughUnchanged()
    {
        // 'A' key. Unlike the old Ctrl-only resolver this superseded, an unrecognized vkCode is not
        // resolved to null: a chord's open-ended second key (any key at all) has to survive this
        // call intact, and this is the one call every raw keystroke passes through before that
        // comparison happens.
        int result = PhysicalKeyResolver.Normalize(0x41, flags: 0);

        Assert.Equal(0x41, result);
    }

    [Fact]
    public void LeftWin_HasNoGenericFallback_PassesThroughUnchanged()
    {
        // Win has no ambiguous generic VK the way Ctrl/Shift/Alt do; both sides always report their
        // own specific code, so this must not be swept up by any sided-pair fallback.
        int result = PhysicalKeyResolver.Normalize((uint)NativeMethods.VK_LWIN, flags: 0);

        Assert.Equal(NativeMethods.VK_LWIN, result);
    }
}
