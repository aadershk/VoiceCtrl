using System.IO;
using System.Windows;
using VoiceCtrl.Core.Injection;

namespace VoiceCtrl.Core.Tests.Injection;

/// <summary>A pasted Dictation asks Windows to keep it out of clipboard history and cloud sync (VED-49).</summary>
public class ClipboardPrivacyTests
{
    private static int Dword(DataObject data, string format)
    {
        var stream = Assert.IsAssignableFrom<Stream>(data.GetData(format));
        var bytes = new byte[4];
        stream.Position = 0;
        stream.ReadExactly(bytes);
        return BitConverter.ToInt32(bytes);
    }

    [Fact]
    public void PrivateText_CarriesTheTextAndTheThreeOptOuts()
    {
        DataObject data = ClipboardPasteInjector.PrivateText("Meet at noon.");

        Assert.Equal("Meet at noon.", data.GetData(DataFormats.UnicodeText));
        Assert.Equal(1, Dword(data, "ExcludeClipboardContentFromMonitorProcessing"));
        Assert.Equal(0, Dword(data, "CanIncludeInClipboardHistory"));
        Assert.Equal(0, Dword(data, "CanUploadToCloudClipboard"));
    }
}
