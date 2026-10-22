using System.IO;
using System.Runtime.CompilerServices;

namespace VoiceCtrl.Core.Tests;

/// <summary>
/// Where the repo root is, for tests that read source files (palettes, XAML, code-behind) directly
/// instead of taking a dependency on the WPF project. Derived from this file's own compile-time
/// path rather than the caller's, so it stays correct however deeply a test class is nested.
/// </summary>
internal static class RepoPaths
{
    public static string Root { get; } = Locate();

    // tests/VoiceCtrl.Core.Tests/RepoPaths.cs -> repo root
    private static string Locate([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
