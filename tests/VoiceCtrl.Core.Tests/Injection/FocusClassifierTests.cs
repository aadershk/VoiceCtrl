using VoiceCtrl.Core.Injection;

namespace VoiceCtrl.Core.Tests.Injection;

/// <summary>"Select a textbox first" (VED-75): the paste goes to the clipboard only when the focus
/// is positively known not to take text. Anything unclear pastes as before, because a wrong
/// "no text box" would leave a Dictation unpasted that would have landed.</summary>
public class FocusClassifierTests
{
    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    public void TheDesktopAndTheTaskbar_TakeNoText(string foregroundClass)
    {
        Assert.Equal(FocusKind.NotTextInput, FocusClassifier.Classify(foregroundClass, controlType: null, isReadOnly: null));
    }

    [Theory]
    [InlineData("ControlType.Button")]
    [InlineData("ControlType.CheckBox")]
    [InlineData("ControlType.RadioButton")]
    [InlineData("ControlType.Hyperlink")]
    [InlineData("ControlType.Image")]
    [InlineData("ControlType.MenuItem")]
    [InlineData("ControlType.TabItem")]
    public void AControlThatNeverTakesText_IsNotATextInput(string controlType)
    {
        Assert.Equal(FocusKind.NotTextInput, FocusClassifier.Classify("Notepad", controlType, isReadOnly: null));
    }

    [Theory]
    [InlineData("ControlType.Edit", false)]
    [InlineData("ControlType.Edit", null)]
    [InlineData("ControlType.Document", false)]
    [InlineData("ControlType.ComboBox", false)]
    public void AnEditableField_IsATextInput(string controlType, bool? readOnly)
    {
        Assert.Equal(FocusKind.TextInput, FocusClassifier.Classify("Chrome_WidgetWin_1", controlType, readOnly));
    }

    [Fact]
    public void AReadOnlyEdit_IsNotATextInput()
    {
        Assert.Equal(FocusKind.NotTextInput, FocusClassifier.Classify("Notepad", "ControlType.Edit", isReadOnly: true));
    }

    [Fact]
    public void AReadOnlyDocument_IsNotJudged_BecauseABrowserPageIsOne()
    {
        // Chromium reports a whole page as a read-only Document, and a contenteditable inside it
        // looks the same from here.
        Assert.Equal(FocusKind.Unknown, FocusClassifier.Classify("Chrome_WidgetWin_1", "ControlType.Document", isReadOnly: true));
    }

    [Theory]
    [InlineData("ControlType.Pane")]
    [InlineData("ControlType.Custom")]
    [InlineData("ControlType.DataItem")]
    [InlineData("ControlType.ListItem")]
    [InlineData("ControlType.Group")]
    [InlineData(null)]
    public void WhatCouldTakeAPaste_IsUnknown_SoItPastesAsBefore(string? controlType)
    {
        Assert.Equal(FocusKind.Unknown, FocusClassifier.Classify("Excel7", controlType, isReadOnly: null));
    }

    [Theory]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS")]
    [InlineData("ConsoleWindowClass")]
    public void ATerminal_TakesAPaste_ThoughItsTextAreaSaysText(string foregroundClass)
    {
        // Windows Terminal's TermControl, the element focused while you type in it, reports
        // ControlType.Text with no ValuePattern (read with UI Automation, 2026-10-22). The owner's
        // dictations into a terminal were left on the clipboard as "no text box".
        Assert.NotEqual(FocusKind.NotTextInput, FocusClassifier.Classify(foregroundClass, "ControlType.Text", isReadOnly: null));
    }

    [Fact]
    public void AnUnreadableForegroundClass_IsUnknown()
    {
        Assert.Equal(FocusKind.Unknown, FocusClassifier.Classify(null, null, null));
    }
}
