// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Xml.Linq;
using AnakinRaW.CommonUtilities.Hashing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PG.Commons;
using PG.StarWarsGame.Engine;
using PG.StarWarsGame.Files.ALO;
using PG.StarWarsGame.Files.MEG;
using PG.StarWarsGame.Files.MTD;
using PG.StarWarsGame.Files.XML;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Providers;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: BaselineBuilder <game-path> <output-file> [schema-path]");
    Console.Error.WriteLine("  game-path   Path to the game installation directory (contains StarWarsG.exe)");
    Console.Error.WriteLine("  output-file Output path for the .baseline binary file");
    Console.Error.WriteLine("  schema-path Path to schema/eaw/ directory (optional, auto-detected if omitted)");
    return 1;
}

var gamePath = args[0];
var outputFile = args[1];
var schemaPath = args.Length >= 3 ? args[2] : FindSchemaPath();

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddSingleton<IFileSystem, FileSystem>();
services.AddSingleton<IHashingService>(sp => new HashingService(sp));
PetroglyphCommons.ContributeServices(services);
services.SupportMEG();
services.SupportALO();
services.SupportXML();
services.SupportMTD();
PetroglyphEngineServiceContribution.ContributeServices(services);

if (schemaPath is not null && Directory.Exists(schemaPath))
{
    services.AddSingleton<ISchemaProvider>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<LocalFileSchemaProvider>>();
        var provider = new LocalFileSchemaProvider(schemaPath, logger);
        provider.Load();
        return provider;
    });
    Console.WriteLine($"Schema: {schemaPath}");
}
else
{
    Console.Error.WriteLine(
        $"Schema directory not found ({schemaPath ?? "none"}) — type names will fall back to GameObjectType.");
}

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

// Read GameConstants.xml via the engine's virtual file system (handles MEG archives)
string? gameConstantsXml = null;
using (var stream = engine.GameRepository.TryOpenFile("Data\\XML\\GameConstants.xml"))
{
    if (stream is not null)
        gameConstantsXml = await new StreamReader(stream).ReadToEndAsync();
}

var schemaProvider = sp.GetService<ISchemaProvider>();
var projector = new GameSymbolProjector(schemaProvider ?? new NullSchemaProvider());
var baseline = projector.Project(gameObjects, sfxEvents, gameConstantsXml, manifestHash);
Console.WriteLine(
    $"Projected {baseline.Symbols.Count} symbols, {baseline.DynamicEnumValues.Count} dynamic enum(s), {baseline.HardcodedEnumValues.Sum(kv => kv.Value.Length)} hardcoded enum value(s)");

// Build file-type registry from game metafiles
var fileTypeMapBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
if (schemaProvider is not null)
{
    foreach (var def in schemaProvider.AllMetafiles)
    {
        if (def.MetafileType == MetafileType.Special) continue;

        if (def.MetafileType == MetafileType.FileRegistry)
        {
            var enginePath = def.Path.ToUpperInvariant().Replace('/', '\\');
            using var stream = engine.GameRepository.TryOpenFile(enginePath);
            if (stream is not null)
            {
                try
                {
                    var xmlContent = await new StreamReader(stream).ReadToEndAsync();
                    var xdoc = XDocument.Parse(xmlContent);
                    foreach (var elem in xdoc.Descendants()
                                 .Where(e => e.Name.LocalName.Equals("File", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Game files use element text content: <File>path</File>.
                        var filename = elem.Value?.Trim();
                        if (string.IsNullOrEmpty(filename))
                            filename = elem.Attributes()
                                .FirstOrDefault(a => a.Name.LocalName.Equals("filename", StringComparison.OrdinalIgnoreCase))?.Value;
                        if (!string.IsNullOrEmpty(filename))
                        {
                            var normalizedPath = filename.Replace('\\', '/').ToLowerInvariant().TrimStart('/');
                            fileTypeMapBuilder[normalizedPath] = [.. def.Types];
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Failed to parse metafile '{def.Path}': {ex.Message}");
                }
            }
        }
        else // DirectContent — the file itself is the content
        {
            fileTypeMapBuilder[def.Path] = [.. def.Types];
        }
    }
}

baseline = baseline with { FileTypeMap = fileTypeMapBuilder.ToImmutable() };
Console.WriteLine($"File type registry: {baseline.FileTypeMap.Count} file(s) registered");

// Serialize
var data = BaselineSerializer.Serialize(baseline);

var outputDir = Path.GetDirectoryName(outputFile);
if (!string.IsNullOrEmpty(outputDir))
    Directory.CreateDirectory(outputDir);

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

// Walk up from the binary's directory until we find schema/eaw
static string? FindSchemaPath()
{
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 8; i++)
    {
        var candidate = Path.Combine(dir, "schema", "eaw");
        if (Directory.Exists(candidate))
            return candidate;
        var parent = Directory.GetParent(dir)?.FullName;
        if (parent is null) break;
        dir = parent;
    }

    return null;
}

// Minimal fallback when no schema is wired — all types become GameObjectType.
file sealed class NullSchemaProvider : ISchemaProvider
{
    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public XmlTagDefinition? GetTag(string t)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string t)
    {
        return [];
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string t)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string e)
    {
        return null;
    }

    public GameObjectTypeDefinition? GetObjectType(string t)
    {
        return null;
    }
}