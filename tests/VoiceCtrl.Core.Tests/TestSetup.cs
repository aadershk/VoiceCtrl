using System.IO;
using System.Runtime.CompilerServices;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Tests;

internal static class TestSetup
{
    // Runs once before any test in this assembly. Without this, tests that exercise real
    // AdaptiveTranscriptionClient/TranscriptionModeStore code paths (not fakes) would write
    // diagnostic log lines into the same log.txt a real running instance uses, making the log
    // useless as evidence during a live debugging session.
    [ModuleInitializer]
    internal static void RedirectLogAwayFromRealUser() =>
        SimpleFileLogger.LogFilePath = Path.Combine(Path.GetTempPath(), "VoiceCtrl.Core.Tests.log.txt");
}
