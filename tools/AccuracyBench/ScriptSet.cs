using System.Text.Json;

namespace VoiceCtrl.Tools.AccuracyBench;

/// <summary>One spoken script. <paramref name="Terms"/> are the names and jargon inside it, the
/// words a Dictionary entry would exist for.</summary>
public sealed record TestScript(string Id, string Category, string Text, string[] Terms);

/// <summary>Where the test set lives and how to read it.</summary>
public sealed class ScriptSet
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public ScriptSet(string root)
    {
        Root = root;
        Scripts = JsonSerializer.Deserialize<TestScript[]>(File.ReadAllText(ScriptsPath), Json)
            ?? throw new InvalidDataException("scripts.json is empty.");
    }

    public string Root { get; }

    public IReadOnlyList<TestScript> Scripts { get; }

    public string ScriptsPath => Path.Combine(Root, "scripts.json");

    /// <summary>Recorded clips. Git-ignored: the owner's voice is never committed.</summary>
    public string AudioDirectory => Path.Combine(Root, "audio");

    public string RunsDirectory => Path.Combine(Root, "runs");

    public string AudioPath(TestScript script) => Path.Combine(AudioDirectory, script.Id + ".wav");

    /// <summary>The accuracy folder, found by walking up to the solution from the working folder, or
    /// from the build output when it was built inside the repo.</summary>
    public static string FindRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "VoiceCtrl.sln")))
                {
                    // The held-out set (ticket 36b) is a second folder of the same shape, picked by
                    // an environment variable so every command works on it unchanged.
                    string folder = Environment.GetEnvironmentVariable("VOICECTRL_BENCH_SET") == "heldout"
                        ? "accuracy-heldout"
                        : "accuracy";
                    return Path.Combine(dir.FullName, ".scratch", "v4-ship", folder);
                }
            }
        }

        throw new DirectoryNotFoundException("VoiceCtrl.sln not found above the working folder; run from inside the repo.");
    }

    /// <summary>Problems with the scripts themselves: length outside 30-60 words, a term that is
    /// not in its own text, a duplicated id. Empty when the set is sound.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        foreach (var duplicate in Scripts.GroupBy(s => s.Id).Where(g => g.Count() > 1))
        {
            problems.Add($"{duplicate.Key}: id used {duplicate.Count()} times");
        }

        foreach (TestScript script in Scripts)
        {
            int words = TextScoring.Words(script.Text, keepCase: false).Length;
            if (words is < 30 or > 60)
            {
                problems.Add($"{script.Id}: {words} words (want 30-60)");
            }

            foreach (string term in script.Terms.Where(t => !TextScoring.TermFound(t, script.Text, keepCase: true)))
            {
                problems.Add($"{script.Id}: term \"{term}\" is not in the text");
            }
        }

        return problems;
    }

    /// <summary>Every distinct term across the set, the Dictionary an owner who cared about these
    /// words would have.</summary>
    public IReadOnlyList<string> AllTerms() =>
        Scripts.SelectMany(s => s.Terms).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
}
