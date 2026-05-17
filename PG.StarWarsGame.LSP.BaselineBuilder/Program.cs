using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.Engine;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Schema;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: BaselineBuilder <game-path> <output-file>");
    Console.Error.WriteLine("  game-path   Path to the game installation directory (contains StarWarsG.exe)");
    Console.Error.WriteLine("  output-file Output path for the .baseline binary file");
    return 1;
}

var gamePath    = args[0];
var outputFile  = args[1];

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
PetroglyphEngineServiceContribution.ContributeServices(services);
var sp = services.BuildServiceProvider();

var engineService = sp.GetRequiredService<IPetroglyphStarWarsGameEngineService>();

Console.WriteLine($"Initializing engine from: {gamePath}");
var locations = new GameLocations(gamePath, gamePath);
var engine = await engineService.InitializeAsync(GameEngineType.Foc, locations);

// Compute source manifest hash from StarWarsG.exe + PerceptionFunctionG.dll
var manifestHash = ComputeManifestHash(gamePath);
Console.WriteLine($"Manifest hash: {manifestHash}");

// Adapt engine entries to ProjectableEntry
var gameObjects = engine.GameObjectTypeManager.GameObjectTypes
    .Select(t => new ProjectableEntry(t.Name, t.ClassificationName, t.Location))
    .ToList();
Console.WriteLine($"Game object types: {gameObjects.Count}");

var sfxEvents = engine.SfxGameManager.Entries
    .Select(s => new ProjectableEntry(s.Name, "SFXEVENT", s.Location))
    .ToList();
Console.WriteLine($"SFX events: {sfxEvents.Count}");

// Load gameconstants.xml for dynamic enums
string? gameConstantsXml = null;
var gcPath = Path.Combine(gamePath, "Data", "XML", "GameConstants.xml");
if (File.Exists(gcPath))
    gameConstantsXml = await File.ReadAllTextAsync(gcPath);

// Project
var schemaProvider = sp.GetService<ISchemaProvider>();
if (schemaProvider is null)
{
    Console.Error.WriteLine("No ISchemaProvider registered — type names will fall back to GameObjectType.");
    schemaProvider = new NullSchemaProvider();
}

var projector = new GameSymbolProjector(schemaProvider);
var baseline  = projector.Project(gameObjects, sfxEvents, gameConstantsXml, manifestHash);
Console.WriteLine($"Projected {baseline.Symbols.Count} symbols, {baseline.DynamicEnumValues.Count} dynamic enum(s)");

// Serialize
var data = BaselineSerializer.Serialize(baseline);
await File.WriteAllBytesAsync(outputFile, data);
Console.WriteLine($"Written: {outputFile} ({data.Length:N0} bytes)");

// Write manifest sidecar
var manifestFile = Path.ChangeExtension(outputFile, ".manifest.json");
await File.WriteAllTextAsync(manifestFile,
    $$"""{ "version": 1, "hash": "{{Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()}}" }""");
Console.WriteLine($"Manifest: {manifestFile}");

return 0;

static string ComputeManifestHash(string gamePath)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var name in new[] { "StarWarsG.exe", "PerceptionFunctionG.dll" })
    {
        var path = Path.Combine(gamePath, name);
        if (File.Exists(path))
            hash.AppendData(File.ReadAllBytes(path));
    }
    return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
}

// Minimal fallback when no schema is wired — all types become GameObjectType.
file sealed class NullSchemaProvider : ISchemaProvider
{
    public event EventHandler? SchemaRefreshed { add { } remove { } }
    public IReadOnlyList<XmlTagDefinition>         AllTags        => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition>           AllEnums       => [];
    public XmlTagDefinition?                       GetTag(string t)              => null;
    public IReadOnlyList<XmlTagDefinition>         GetAllTagDefinitions(string t) => [];
    public IReadOnlyList<XmlTagDefinition>         GetTagsForType(string t)       => [];
    public EnumDefinition?                         GetEnum(string e)              => null;
    public GameObjectTypeDefinition?               GetObjectType(string t)        => null;
}
