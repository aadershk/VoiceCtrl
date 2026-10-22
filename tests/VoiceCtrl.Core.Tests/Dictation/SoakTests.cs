using VoiceCtrl.Core.Dictation;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>
/// The whole-engine reliability checks of the evaluation matrix (VED-49): hundreds of Dictations in
/// a row with managed memory staying flat, and rapid, random gestures never wedging the engine.
/// Runs the real <see cref="DictationController"/> against the fakes in <see cref="Rig"/>.
/// </summary>
[Collection("MemorySensitive")]
public class SoakTests
{
    private const int Dictations = 600;

    [Fact]
    public async Task SixHundredDictations_KeepKeptListsBounded_AndMemoryFlat()
    {
        var rig = new Rig();
        long baseline = 0;

        for (int i = 0; i < Dictations; i++)
        {
            if (rig.Phase == OverlayPhase.Hidden)
            {
                await rig.Controller.ToggleBar();
            }

            // Mostly successes; every 7th fails (cycling the failure kinds), every 11th is cancelled.
            if (i % 11 == 10)
            {
                await rig.Controller.Tap();
                rig.Controller.Cancel();
            }
            else
            {
                if (i % 7 == 6)
                {
                    rig.Transcriber.Throws((i / 7 % 3) switch { 0 => Rig.NoInternet(), 1 => Rig.TimedOut(), _ => Rig.ServiceError() });
                }
                else
                {
                    rig.Transcriber.Returns($"Soak dictation number {i} with a few words to keep the history busy.");
                }

                await rig.Controller.Tap();
                await rig.Controller.Tap();
            }

            // Let the Pasted/Failed notice time out, as it would in real time.
            rig.Timer.Advance(TimeSpan.FromSeconds(10));

            if (i == 100)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                baseline = GC.GetTotalMemory(forceFullCollection: true);
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long end = GC.GetTotalMemory(forceFullCollection: true);

        // Not vacuous: ~4 in 5 Dictations paste, and the failing ones were kept (capped).
        Assert.InRange(rig.Injector.Pasted.Count, 450, Dictations);
        Assert.NotEmpty(rig.Controller.FailedDictations);
        Assert.False(rig.Recorder.IsRecording);
        Assert.True(rig.History.Entries.Count <= DictationHistoryStoreLimit, $"history kept {rig.History.Entries.Count}");
        Assert.True(rig.Controller.FailedDictations.Count <= FailedDictationList.Capacity);

        // 500 more Dictations may not grow the managed heap by more than a few MB (bounded lists
        // and caches only; a per-Dictation leak would be tens of MB).
        long growth = end - baseline;
        Assert.True(growth < 8_000_000, $"managed heap grew {growth:N0} bytes over {Dictations - 100} Dictations");
    }

    [Fact]
    public async Task RapidRandomGestures_NeverThrow_AndTheEngineAlwaysRecovers()
    {
        var rig = new Rig();
        var random = new Random(4_0_0);

        for (int i = 0; i < 6000; i++)
        {
            switch (random.Next(6))
            {
                case 0: await rig.Controller.ToggleBar(); break;
                case 1: await rig.Controller.Tap(); break;
                case 2: await rig.Controller.HoldStart(); break;
                case 3: await rig.Controller.HoldEnd(); break;
                case 4: rig.Controller.Cancel(); break;
                default: rig.Timer.AdvanceMs(random.Next(1, 1500)); break;
            }

            // Never recording while the Overlay says it isn't listening.
            if (rig.Phase != OverlayPhase.Listening)
            {
                Assert.False(rig.Recorder.IsRecording, $"gesture {i}: recording during {rig.Phase}");
            }
        }

        // The storm really recorded and pasted things, so the checks above weren't vacuous.
        Assert.True(rig.Recorder.Starts > 100, $"only {rig.Recorder.Starts} recordings started");

        // Whatever state the storm left, Esc and time bring it home, and a normal Dictation works.
        rig.Controller.Cancel();
        rig.Timer.Advance(TimeSpan.FromSeconds(30));
        rig.Controller.Cancel();
        rig.Timer.Advance(TimeSpan.FromSeconds(30));
        if (rig.Phase != OverlayPhase.Hidden)
        {
            await rig.Controller.ToggleBar();
        }

        rig.Injector.Pasted.Clear();
        rig.Transcriber.Returns("after the storm");
        if (rig.Phase == OverlayPhase.Hidden)
        {
            await rig.Controller.ToggleBar();
        }

        await rig.Controller.Tap();
        await rig.Controller.Tap();
        Assert.Equal(["after the storm"], rig.Injector.Pasted);
        Assert.False(rig.Recorder.IsRecording);
    }

    private const int DictationHistoryStoreLimit = VoiceCtrl.Core.Transcription.DictationHistoryStore.MaxEntries;
}
