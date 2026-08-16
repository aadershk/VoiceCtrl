using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Interop;
using Xunit;

namespace VoiceCtrl.Core.Tests.Hotkey;

public class TrackedCtrlKeyResolverTests
{
    [Fact]
    public void ExactLeftControlVk_ResolvesToLeftControl()
    {
        int? result = TrackedCtrlKeyResolver.Resolve((uint)NativeMethods.VK_LCONTROL, flags: 0);

        Assert.Equal(NativeMethods.VK_LCONTROL, result);
    }

    [Fact]
    public void ExactRightControlVk_ResolvesToRightControl()
    {
        int? result = TrackedCtrlKeyResolver.Resolve((uint)NativeMethods.VK_RCONTROL, flags: 0);

        Assert.Equal(NativeMethods.VK_RCONTROL, result);
    }

    [Fact]
    public void GenericControlVkWithExtendedFlag_ResolvesToRightControl()
    {
        int? result = TrackedCtrlKeyResolver.Resolve((uint)NativeMethods.VK_CONTROL, NativeMethods.LLKHF_EXTENDED);

        Assert.Equal(NativeMethods.VK_RCONTROL, result);
    }

    [Fact]
    public void GenericControlVkWithoutExtendedFlag_ResolvesToLeftControl()
    {
        int? result = TrackedCtrlKeyResolver.Resolve((uint)NativeMethods.VK_CONTROL, flags: 0);

        Assert.Equal(NativeMethods.VK_LCONTROL, result);
    }

    [Fact]
    public void UnrelatedVk_ResolvesToNull()
    {
        // 'A' key.
        int? result = TrackedCtrlKeyResolver.Resolve(0x41, flags: 0);

        Assert.Null(result);
    }
}
