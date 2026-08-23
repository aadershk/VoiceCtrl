using System.Diagnostics;
using System.IO;
using System.Threading;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Tray;

namespace VoiceCtrl.Bootstrap;

/// <summary>
/// One-time setup on first launch: attaches a console to interactively ask for a transcription
/// mode and (for Online) a Gemini API key, writes .env from the answers, and turns on autostart.
/// A blank GEMINI_API_KEY (rather than copying placeholder text) means AppConfig.IsApiKeyConfigured
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

        NativeMethods.AllocConsole();
        try
        {
            RunInteractiveSetup(envPath);
        }
        finally
        {
            NativeMethods.FreeConsole();
        }

        AutoStartManager.Enable();

        return true;
    }

    private static void RunInteractiveSetup(string envPath)
    {
        Console.WriteLine("===================================================");
        Console.WriteLine(" VoiceCtrl first-run setup");
        Console.WriteLine("===================================================");
        Console.WriteLine();
        Console.WriteLine("VoiceCtrl has two transcription modes:");
        Console.WriteLine("  [1] Online:  Google Gemini. Best accuracy, needs your own free API key.");
        Console.WriteLine("  [2] Offline: runs fully on this PC. No key, no internet, no account.");
        Console.WriteLine("               Downloads a ~700MB speech model the first time you use it.");
        Console.WriteLine();
        Console.Write("Choose a mode [1=Online (default), 2=Offline]: ");

        string? modeAnswer = Console.ReadLine();
        bool offline = modeAnswer?.Trim() == "2";

        string apiKey = string.Empty;
        if (!offline)
        {
            Console.WriteLine();
            Console.WriteLine("Get a free Gemini API key at: https://aistudio.google.com/apikey");
            Console.Write("Paste your API key (or press Enter to add it later): ");
            apiKey = Console.ReadLine()?.Trim() ?? string.Empty;
        }

        File.WriteAllText(envPath, BuildEnvContents(offline, apiKey));

        Console.WriteLine();
        if (offline)
        {
            Console.WriteLine("Offline mode selected. The speech model downloads on first use.");
        }
        else if (string.IsNullOrEmpty(apiKey))
        {
            Console.WriteLine("No key entered. Add one later via the tray icon's Settings menu.");
        }
        else
        {
            Console.WriteLine("Online mode configured.");
        }

        Console.WriteLine("Setup complete. VoiceCtrl is now running in your system tray.");
        Console.WriteLine("This window will close in a few seconds...");
        Thread.Sleep(3000);
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
}
