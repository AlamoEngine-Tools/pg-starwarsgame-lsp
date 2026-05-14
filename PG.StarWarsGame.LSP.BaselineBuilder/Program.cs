using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.Engine;
using PG.StarWarsGame.LSP.Core.Symbols;

// args: --game-path <path> --variant <EaW|FoC> --output <file>
var gamePath = Arg("--game-path");
var variantStr = Arg("--variant") ?? "FoC";
var output = Arg("--output") ?? "baseline.json";

if (string.IsNullOrWhiteSpace(gamePath))
{
    Console.Error.WriteLine("Usage: BaselineBuilder --game-path <path> [--variant EaW|FoC] [--output <file>]");
    return 1;
}

if (!Directory.Exists(gamePath))
{
    Console.Error.WriteLine($"Error: game path does not exist: {gamePath}");
    return 2;
}

var engineType = string.Equals(variantStr, "EaW", StringComparison.OrdinalIgnoreCase)
    ? GameEngineType.Eaw
    : GameEngineType.Foc;

Console.WriteLine($"Scanning {variantStr} at {gamePath}…");

var services = new ServiceCollection();
services.AddLogging();
PetroglyphEngineServiceContribution.ContributeServices(services);
var provider = services.BuildServiceProvider();
var engineService = provider.GetRequiredService<IPetroglyphStarWarsGameEngineService>();

var locations = new GameLocations(
    Array.Empty<string>(),
    gamePath,
    Array.Empty<string>());

var engine = await engineService.InitializeAsync(engineType, locations);

var symbols = new List<SerializedSymbol>();
foreach (var entry in engine.GameObjectTypeManager.Entries)
    symbols.Add(new SerializedSymbol
    {
        Name = entry.Name,
        TypeName = entry.ClassificationName,
        FilePath = entry.Location.XmlFile ?? string.Empty,
        Line = entry.Location.Line ?? 0
    });

var baseline = new SerializedBaseline
{
    GameVariant = variantStr,
    GameVersion = "unknown",
    BuildDate = DateTimeOffset.UtcNow,
    Symbols = symbols
};

var json = JsonSerializer.Serialize(baseline, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(output, json);
Console.WriteLine($"Wrote {symbols.Count} symbols to {output}");
return 0;

static string? Arg(string name)
{
    var args = Environment.GetCommandLineArgs();
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}