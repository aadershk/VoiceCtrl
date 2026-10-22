using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Injection;

/// <summary>
/// Reads what has the keyboard focus right now through UI Automation and hands it to
/// <see cref="FocusClassifier"/>. UI Automation calls cross into the other app's process and can
/// stall on a busy one, so the read runs off the UI thread under a short deadline; a read that
/// fails or runs out of time is <see cref="FocusKind.Unknown"/>, which pastes as before.
/// </summary>
public static class FocusInspector
{
    public static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(250);

    public static async Task<FocusKind> InspectAsync()
    {
        try
        {
            return await Task.Run(Read).WaitAsync(Deadline).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            return FocusKind.Unknown;
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("FocusInspector", ex);
            return FocusKind.Unknown;
        }
    }

    private static FocusKind Read()
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return FocusKind.Unknown;
        }

        var className = new StringBuilder(256);
        string? foregroundClass = GetClassName(foreground, className, className.Capacity) > 0 ? className.ToString() : null;

        string? controlType = null;
        bool? isReadOnly = null;
        try
        {
            AutomationElement? focused = AutomationElement.FocusedElement;
            if (focused is not null)
            {
                controlType = focused.Current.ControlType.ProgrammaticName;
                if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern) && pattern is ValuePattern value)
                {
                    isReadOnly = value.Current.IsReadOnly;
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            // The element went away between asking and reading: nothing known about it.
        }

        return FocusClassifier.Classify(foregroundClass, controlType, isReadOnly);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
