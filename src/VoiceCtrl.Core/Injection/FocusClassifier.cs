namespace VoiceCtrl.Core.Injection;

/// <summary>What the focus looks like to the paste (VED-75, "Select a textbox first").</summary>
public enum FocusKind
{
    /// <summary>Can't tell, or it could take a paste: paste as before.</summary>
    Unknown,
    TextInput,

    /// <summary>Known not to take text: the desktop, a button, a read-only field.</summary>
    NotTextInput,
}

/// <summary>
/// Decides, from what UI Automation reports about the focused element, whether a paste has
/// anywhere to land. Deliberately one-sided: it says <see cref="FocusKind.NotTextInput"/> only when
/// sure, because a false "no text box" leaves the text on the clipboard instead of in the app, and
/// browsers, Electron apps and spreadsheets all report their editable parts as generic Panes,
/// Documents, Custom or DataItem elements. Text isn't on the never list for the same reason:
/// Windows Terminal's typing area reports ControlType.Text, and a plain label rarely has the focus.
/// </summary>
public static class FocusClassifier
{
    private static readonly HashSet<string> NeverTakeText = new(StringComparer.Ordinal)
    {
        "ControlType.Button",
        "ControlType.SplitButton",
        "ControlType.CheckBox",
        "ControlType.RadioButton",
        "ControlType.Hyperlink",
        "ControlType.Image",
        "ControlType.Menu",
        "ControlType.MenuBar",
        "ControlType.MenuItem",
        "ControlType.Tab",
        "ControlType.TabItem",
        "ControlType.ScrollBar",
        "ControlType.Slider",
        "ControlType.ProgressBar",
        "ControlType.TitleBar",
        "ControlType.ToolBar",
        "ControlType.StatusBar",
        "ControlType.Thumb",
        "ControlType.Separator",
    };

    /// <param name="foregroundClass">The foreground window's class name, or null if unreadable.</param>
    /// <param name="controlType">The focused element's <c>ControlType.ProgrammaticName</c>, or null.</param>
    /// <param name="isReadOnly">Its ValuePattern's IsReadOnly, or null when it has none.</param>
    public static FocusKind Classify(string? foregroundClass, string? controlType, bool? isReadOnly)
    {
        if (string.IsNullOrEmpty(foregroundClass))
        {
            return FocusKind.Unknown;
        }

        if (!PasteTargetFilter.IsPasteTarget(foregroundClass))
        {
            return FocusKind.NotTextInput;
        }

        switch (controlType)
        {
            case null:
                return FocusKind.Unknown;

            case "ControlType.Edit" or "ControlType.ComboBox":
                return isReadOnly == true ? FocusKind.NotTextInput : FocusKind.TextInput;

            // A browser page is a read-only Document too, with a contenteditable looking the same.
            case "ControlType.Document":
                return isReadOnly == true ? FocusKind.Unknown : FocusKind.TextInput;

            default:
                return NeverTakeText.Contains(controlType) ? FocusKind.NotTextInput : FocusKind.Unknown;
        }
    }
}
