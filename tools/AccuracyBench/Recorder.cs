using NAudio.CoreAudioApi;
using VoiceCtrl.Core.Audio;

namespace VoiceCtrl.Tools.AccuracyBench;

/// <summary>
/// The reading session: shows one script at a time, records it through the same capture path the
/// app uses (so the numbers describe the app, not a nicer recorder), and keeps the clip only when
/// the reader says so. Safe to stop and resume: clips already on disk are skipped.
/// </summary>
public static class Recorder
{
    public static int Run(ScriptSet set)
    {
        Directory.CreateDirectory(set.AudioDirectory);
        using var enumerator = new MMDeviceEnumerator();
        string device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).FriendlyName;

        Console.WriteLine($"Microphone: {device}");
        Console.WriteLine("Read each script at your normal dictation pace, as if typing it by voice.");
        Console.WriteLine("Enter = start, Enter again = stop. Then Enter = keep, r = redo. s = skip, q = quit (progress is saved).");
        Console.WriteLine();

        var recorder = new WasapiAudioRecorder(() => null);
        foreach (TestScript script in set.Scripts)
        {
            string path = set.AudioPath(script);
            if (File.Exists(path))
            {
                continue;
            }

            while (true)
            {
                Console.WriteLine($"--- {script.Id} of {set.Scripts.Count}  ({script.Category}) ---");
                Console.WriteLine();
                Console.WriteLine(script.Text);
                Console.WriteLine();
                Console.Write("Enter to start (s skip, q quit): ");
                string start = (Console.ReadLine() ?? "q").Trim().ToLowerInvariant();
                if (start == "q")
                {
                    return 0;
                }

                if (start == "s")
                {
                    break;
                }

                recorder.Start();
                Console.Write("Recording... Enter to stop: ");
                Console.ReadLine();
                AudioClip clip = recorder.StopAsync().GetAwaiter().GetResult();

                Console.Write($"{clip.Duration.TotalSeconds:0.0} s. Enter to keep, r to redo: ");
                if ((Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant() == "r")
                {
                    continue;
                }

                File.WriteAllBytes(path, clip.WavBytes);
                Console.WriteLine("Saved.");
                Console.WriteLine();
                break;
            }
        }

        int recorded = set.Scripts.Count(s => File.Exists(set.AudioPath(s)));
        Console.WriteLine($"Done: {recorded} of {set.Scripts.Count} clips on disk.");
        return 0;
    }
}
