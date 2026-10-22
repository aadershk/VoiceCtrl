using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Tools.BridgeTypegen;

// Regenerates ui/src/bridge/contracts.g.ts from the C# bridge contracts (spec §4).
// Usage: dotnet run --project tools/BridgeTypegen [path to contracts.g.ts]

string target = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(FindRepoRoot(), "ui", "src", "bridge", "contracts.g.ts");
string generated = TypeScriptWriter.Write(ContractCatalog.Read(typeof(BridgeDispatcher).Assembly));

File.WriteAllText(target, generated);
Console.WriteLine($"Wrote {target}");

static string FindRepoRoot()
{
    for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "VoiceCtrl.sln")))
        {
            return dir.FullName;
        }
    }

    throw new InvalidOperationException("Run this from inside the VoiceCtrl repo, or pass the output path.");
}
