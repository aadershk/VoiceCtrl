using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Tools.BridgeTypegen;

namespace VoiceCtrl.Core.Tests.Bridge;

/// <summary>
/// Guards the contracts themselves (spec §4): every one is serializable through the source-generated
/// context, names are unique and well-formed, and the checked-in <c>contracts.g.ts</c> matches what
/// the C# records generate today.
/// </summary>
public partial class BridgeContractTests
{
    private static readonly Assembly Core = typeof(BridgeDispatcher).Assembly;

    [GeneratedRegex("^[a-z]+\\.[a-z][A-Za-z]*$")]
    private static partial Regex WireName();

    [Fact]
    public void EveryContractType_IsInTheJsonContext()
    {
        List<Type> missing = ContractCatalog.Read(Core).AllTypes
            .Where(type => !type.IsEnum && BridgeJsonContext.Default.GetTypeInfo(type) is null)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryContractEnum_TravelsAsACamelCaseString()
    {
        // An enum without a converter goes over the wire as a number, and the page reads it as
        // undefined (the trial found Settings saying "undefined API key" for OnlineProvider).
        List<string> wrong = [];
        foreach (Type type in ContractCatalog.Read(Core).AllTypes.Where(type => type.IsEnum))
        {
            object first = Enum.GetValues(type).GetValue(0)!;
            string expected = JsonNamingPolicy.CamelCase.ConvertName(first.ToString()!);
            string json;
            try
            {
                json = JsonSerializer.Serialize(first, type, BridgeJsonContext.Default.Options);
            }
            catch (Exception e) when (e is NotSupportedException or InvalidOperationException)
            {
                json = e.GetType().Name;
            }

            if (json != $"\"{expected}\"")
            {
                wrong.Add($"{type.Name}: {json}");
            }
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void WireNames_AreUniqueAndNamespaced()
    {
        ContractCatalog catalog = ContractCatalog.Read(Core);
        List<string> names = [.. catalog.Commands.Select(c => c.Name), .. catalog.Events.Select(e => e.Name)];

        Assert.All(names, name => Assert.Matches(WireName(), name));
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void CheckedInTypeScript_IsFresh()
    {
        string path = Path.Combine(RepoPaths.Root, "ui", "src", "bridge", "contracts.g.ts");
        string checkedIn = File.ReadAllText(path).ReplaceLineEndings("\n");

        string generated = TypeScriptWriter.Write(ContractCatalog.Read(Core));

        // On failure: run `dotnet run --project tools/BridgeTypegen` and commit the result.
        Assert.Equal(generated, checkedIn);
    }
}
