using System.Diagnostics;
using System.IO;
using VoiceCtrl.Tray;

namespace VoiceCtrl.Bootstrap;

/// <summary>
/// One-time setup on first launch: shows a small WPF wizard to pick a transcription mode and
/// (for Online) a Gemini API key, writes .env from the answers, and turns on autostart. A blank
/// GEMINI_API_KEY (rather than copying placeholder text) means AppConfig.IsApiKeyConfigured
/// correctly reports false until the user actually fills one in.
/// </summary>
public static class FirstRunSetup
{
    public static bool Run(string envPath)
    {
        if (File.Exists(envPath))
        {
            return false;
        }

        var setupWindow = new SetupWindow();
        setupWindow.ShowDialog();

        File.WriteAllText(envPath, BuildEnvContents(setupWindow.Offline, setupWindow.ApiKey));

        AutoStartManager.Enable();

        return true;
    }

    private static string BuildEnvContents(bool offline, string apiKey) =>
        $"""
        # VoiceCtrl configuration
        # Get a key from https://aistudio.google.com/apikey
        GEMINI_API_KEY={apiKey}

        # Online = Gemini cloud transcription (needs GEMINI_API_KEY above).
        # Offline = fully local, no key, no internet. Downloads a model on first use.
        TRANSCRIPTION_MODE={(offline ? "Offline" : "Online")}

        # Optional overrides (defaults shown; safe to leave commented out)
        # GEMINI_MODEL_ID=gemini-3.7-flash
        # LOCAL_MODEL_VARIANT=parakeet-tdt-0.6b-v2
        # DOUBLE_TAP_WINDOW_MS=400
        # AUTO_HIDE_DELAY_MS=1500
        # THINKING_LEVEL=low
        # DICTATION_STYLE_NOTES=Prefer "that" over "which"; never expand acronyms like API, UI, URL
        # ENABLE_TONE_AWARENESS=true
        # PROMPT_STYLE_APPS=claude,cursor
        # CHAT_STYLE_APPS=whatsapp,telegram
        # CLEANUP_LEVEL=standard

        # Offline model threads. Defaults to half this machine's logical cores, clamped to 2-8.
        # LOCAL_NUM_THREADS=8

        # MP3-compress audio before uploading in Online mode. Roughly a tenth of the payload,
        # so a faster upload, but the effect on accuracy has not been measured against the live
        # API yet. Leave off unless you have compared transcripts yourself.
        # COMPRESS_UPLOAD=false

        # Offline mode only: turn spoken "comma", "period", "question mark" into the characters.
        # Off by default because those are ordinary words, so "the comma is missing" would become
        # "the , is missing". Online mode still has the audio and needs no such switch.
        # SPOKEN_PUNCTUATION=false

        # Dictionary, snippets and per-app profiles live next to this file, in
        # %LOCALAPPDATA%\VoiceCtrl. Open them from the tray icon under Personalize.
        """;

    public static void OpenInNotepad(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoiceCtrl] Failed to open {path} in Notepad: {ex}");
        }
    }

    /// <summary>Same as OpenInNotepad, but awaits Notepad closing and then invokes onClosed. Used
    /// for .env, where edits need a restart to take effect and the user should be told so.</summary>
    public static async Task OpenInNotepadAndNotifyOnCloseAsync(string path, Action onClosed)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
            if (process is null)
            {
                return;
            }

            await process.WaitForExitAsync().ConfigureAwait(true);
            onClosed();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoiceCtrl] Failed to open {path} in Notepad: {ex}");
        }
    }
}
