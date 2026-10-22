using VoiceCtrl.Core.Audio;
using Xunit;

namespace VoiceCtrl.Core.Tests.Audio;

public class MicrophoneListTests
{
    private static readonly MicrophoneDevice Realtek = new("{realtek}", "Microphone (Realtek Audio)");
    private static readonly MicrophoneDevice Jabra = new("{jabra}", "Headset (Jabra Evolve2 65)");

    private static readonly MicrophoneList TwoMics = new([Realtek, Jabra], Realtek);

    [Fact]
    public void RecordingDevice_NoChoice_IsTheWindowsDefault()
    {
        Assert.Null(TwoMics.RecordingDevice(chosenId: null));
    }

    [Fact]
    public void RecordingDevice_ConnectedChoice_IsThatDevice()
    {
        Assert.Equal("{jabra}", TwoMics.RecordingDevice("{jabra}"));
    }

    [Fact]
    public void RecordingDevice_UnpluggedChoice_FallsBackToTheWindowsDefault()
    {
        // The choice is kept in settings, so plugging the headset back in picks it up again.
        Assert.Null(TwoMics.RecordingDevice("{yeti}"));
    }

    [Fact]
    public void HasMicrophone_FollowsTheList()
    {
        Assert.True(TwoMics.HasMicrophone);
        Assert.False(MicrophoneList.Empty.HasMicrophone);
    }

    [Fact]
    public void SameAs_ComparesContentsNotInstances()
    {
        Assert.True(TwoMics.SameAs(new MicrophoneList([Realtek, Jabra], Realtek)));
        Assert.False(TwoMics.SameAs(new MicrophoneList([Realtek, Jabra], Jabra)));
        Assert.False(TwoMics.SameAs(new MicrophoneList([Realtek], Realtek)));
    }
}
