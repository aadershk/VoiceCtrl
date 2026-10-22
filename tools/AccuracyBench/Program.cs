using VoiceCtrl.Tools.AccuracyBench;

const string Usage = """
    AccuracyBench: the voice accuracy test set (ticket 34).

      check                          validate scripts.json (length, terms)
      record                         read the scripts into the microphone; resumable
      run <pipeline> [--dict]        replay the clips; pipeline = parakeet-v2 | parakeet-v3 | whisper-turbo | gemini
      replay <pipeline>              re-apply the current Dictionary corrector to a saved raw run (no audio)
      hotwords <pipeline> <score> <raw|tokens> [--corr] [--repeats N]   sherpa hotword biasing (ticket 36c); score 0 = greedy control
      score                          print the tables for every saved run
    """;

if (args.Length == 0)
{
    Console.WriteLine(Usage);
    return 2;
}

var set = new ScriptSet(ScriptSet.FindRoot());

switch (args[0])
{
    case "check":
        IReadOnlyList<string> problems = set.Validate();
        problems.ToList().ForEach(Console.WriteLine);
        Console.WriteLine($"{set.Scripts.Count} scripts, {set.Scripts.Sum(s => TextScoring.Words(s.Text, false).Length)} words, " +
            $"{set.Scripts.Sum(s => s.Terms.Length)} term occurrences ({set.AllTerms().Count} distinct), {problems.Count} problems.");
        return problems.Count == 0 ? 0 : 1;

    case "record":
        return Recorder.Run(set);

    case "run" when args.Length >= 2:
        return await Runner.RunAsync(set, args[1], withDictionary: args.Contains("--dict"));

    case "replay" when args.Length >= 2:
        return Runner.Replay(set, args[1]);

    case "hotwords" when args.Length >= 4:
        // hotwords <pipeline> <score> <raw|tokens> [--corr] [--repeats N]
        int repeatsAt = Array.IndexOf(args, "--repeats");
        return HotwordExperiment.Run(set, args[1], double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), args[3],
            args.Contains("--corr"), repeatsAt > 0 ? int.Parse(args[repeatsAt + 1]) : 1);

    case "score":
        Console.WriteLine(Scorer.Report(set, Runner.LoadRuns(set)));
        return 0;

    default:
        Console.WriteLine(Usage);
        return 2;
}
