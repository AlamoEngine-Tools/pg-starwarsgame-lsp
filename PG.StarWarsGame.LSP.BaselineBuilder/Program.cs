// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.CommandLine;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Xml.Linq;
using AnakinRaW.CommonUtilities.Hashing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PG.Commons;
using PG.StarWarsGame.Engine;
using PG.StarWarsGame.Files.ALO;
using PG.StarWarsGame.Files.ALO.Services;
using PG.StarWarsGame.Files.MEG;
using PG.StarWarsGame.Files.MEG.Data.EntryLocations;
using PG.StarWarsGame.Files.MEG.Services;
using PG.StarWarsGame.Files.MTD;
using PG.StarWarsGame.Files.MTD.Services;
using PG.StarWarsGame.Files.XML;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Providers;

// ── Shared options ────────────────────────────────────────────────────────────

var outputOption = new Option<FileInfo>(["--output", "-o"], "Output .baseline file path") { IsRequired = true };
var schemaOption = new Option<DirectoryInfo?>(["--schema", "-s"],
    "Path to schema/eaw/ directory (auto-detected from the binary location if omitted)");

// ── eaw verb ──────────────────────────────────────────────────────────────────

var eawPathOption = new Option<DirectoryInfo>(["--path", "-p"], "EaW game data directory") { IsRequired = true };

var eawCommand = new Command("eaw",
    "Build an Empire at War baseline. " +
    "Note: full EaW engine support is not yet implemented; game-object and SFX projection uses FoC-mode against the EaW path.");
eawCommand.AddOption(eawPathOption);
eawCommand.AddOption(outputOption);
eawCommand.AddOption(schemaOption);
eawCommand.SetHandler(async context =>
{
    var eawPath = context.ParseResult.GetValueForOption(eawPathOption)!.FullName;
    var output  = context.ParseResult.GetValueForOption(outputOption)!.FullName;
    var schema  = context.ParseResult.GetValueForOption(schemaOption)?.FullName ?? FindSchemaPath();
    context.ExitCode = await RunAsync(enginePath: eawPath, eawLayerPath: null, output, schema);
});

// ── foc verb ──────────────────────────────────────────────────────────────────

var focEawOption = new Option<DirectoryInfo>(["--eaw", "-e"],
    "EaW game data directory — loaded first as the base asset layer") { IsRequired = true };
var focFocOption = new Option<DirectoryInfo>(["--foc", "-f"],
    "FoC game data directory") { IsRequired = true };

var focCommand = new Command("foc",
    "Build a Forces of Corruption baseline. EaW assets are loaded first; FoC assets override them.");
focCommand.AddOption(focEawOption);
focCommand.AddOption(focFocOption);
focCommand.AddOption(outputOption);
focCommand.AddOption(schemaOption);
focCommand.SetHandler(async context =>
{
    var eawPath = context.ParseResult.GetValueForOption(focEawOption)!.FullName;
    var focPath = context.ParseResult.GetValueForOption(focFocOption)!.FullName;
    var output  = context.ParseResult.GetValueForOption(outputOption)!.FullName;
    var schema  = context.ParseResult.GetValueForOption(schemaOption)?.FullName ?? FindSchemaPath();
    context.ExitCode = await RunAsync(enginePath: focPath, eawLayerPath: eawPath, output, schema);
});

// ── Root command ──────────────────────────────────────────────────────────────

var rootCommand = new RootCommand("PG Star Wars Game LSP Baseline Builder");
rootCommand.AddCommand(eawCommand);
rootCommand.AddCommand(focCommand);
return await rootCommand.InvokeAsync(args);

// ── Main logic ────────────────────────────────────────────────────────────────

async Task<int> RunAsync(string enginePath, string? eawLayerPath, string outputFile, string? schemaPath)
{
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
            var provider = new LocalFileSchemaProvider(schemaPath,
                sp.GetRequiredService<IFileSystem>(), logger);
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

    Console.WriteLine($"Initializing engine from: {enginePath}");
    // For foc: enginePath=FoC, eawLayerPath=EaW — FocGameRepository loads EaW MEGs from the
    // fallback path first, then FoC MEGs on top. Without the EaW fallback, only FoC-specific
    // game objects are found and EaW-defined objects are absent from the symbol set.
    var locations = new GameLocations(enginePath, eawLayerPath ?? enginePath);
    var engine = await engineService.InitializeAsync(GameEngineType.Foc, locations);

    // Compute source manifest hash from StarWarsG.exe + PerceptionFunctionG.dll
    var manifestHash = ComputeManifestHash(enginePath);
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
    var baseline = projector.Project(gameObjects, sfxEvents, manifestHash);
    Console.WriteLine($"Projected {baseline.Symbols.Count} symbol(s)");

    // ── MEG loading (EaW layer first, then engine layer) ──────────────────────
    //
    // MegLoadOrderResolver ensures non-patch MEGs come first (sorted), then
    // patch.meg → patch2.meg → 64patch.meg.  SFX MEGs are filtered to
    // non_localized + English.  The entry lookup is built with last-wins
    // semantics: EaW entries are written first, engine (FoC) entries overwrite
    // them for the same normalised path.

    var megFileService  = sp.GetRequiredService<IMegFileService>();
    var megExtractor    = sp.GetRequiredService<IMegFileExtractor>();
    var aloFileService  = sp.GetRequiredService<IAloFileService>();
    var mtdFileService  = sp.GetRequiredService<IMtdFileService>();
    var assetLogger     = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(MegAssetCatalogBuilder));

    var entryLookup = new Dictionary<string, MegDataEntryLocationReference>(StringComparer.OrdinalIgnoreCase);
    var megEntries  = new List<(string megName, IEnumerable<string> entryPaths)>();

    // Ordered list: EaW paths (if provided) followed by engine paths.
    var orderedMegPaths = BuildOrderedMegPaths(eawLayerPath, enginePath);
    Console.WriteLine($"MEG archives to scan: {orderedMegPaths.Count}");

    foreach (var megPath in orderedMegPaths)
    {
        try
        {
            var megFile     = megFileService.Load(megPath);
            var entryPaths  = new List<string>(megFile.Archive.Count);
            foreach (var entry in megFile.Archive)
            {
                var normalized = MegAssetCatalogBuilder.NormalizeMegPath(entry.Path);
                entryLookup[normalized] = new MegDataEntryLocationReference(megFile, entry);
                entryPaths.Add(entry.Path);
            }
            megEntries.Add((Path.GetFileName(megPath), entryPaths));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not load MEG '{megPath}': {ex.Message}");
        }
    }

    Func<string, Stream?> openMegEntry = normalizedPath =>
    {
        if (!entryLookup.TryGetValue(normalizedPath, out var locationRef)) return null;
        try { return megExtractor.GetData(locationRef); }
        catch { return null; }
    };

    // ── Dynamic enum extraction ───────────────────────────────────────────────

    Func<string, string?> enumFileReader = path =>
    {
        if (path.Contains("gameconstants", StringComparison.OrdinalIgnoreCase))
            return gameConstantsXml;
        using var enumStream = engine.GameRepository.TryOpenFile(path.ToUpperInvariant().Replace('/', '\\'));
        return enumStream is null ? null : new StreamReader(enumStream).ReadToEnd();
    };

    var (dynEnums, hardEnums) = DynamicEnumExtractor.Extract(
        schemaProvider ?? new NullSchemaProvider(), enumFileReader);
    baseline = baseline with { DynamicEnumValues = dynEnums, HardcodedEnumValues = hardEnums };
    Console.WriteLine(
        $"Dynamic enums: {dynEnums.Count} enum(s), {dynEnums.Sum(kv => kv.Value.Length)} value(s) total");

    // ── Group membership extraction ───────────────────────────────────────────

    var sfxEntries = engine.SfxGameManager.Entries
        .Where(s => !string.IsNullOrEmpty(s.Location.XmlFile))
        .Select(s => (s.Name, s.Location.XmlFile));

    Func<string, string?> groupFileReader = path =>
    {
        using var groupStream = engine.GameRepository.TryOpenFile(path.ToUpperInvariant().Replace('/', '\\'));
        return groupStream is null ? null : new StreamReader(groupStream).ReadToEnd();
    };

    var groupMemberships = GroupMembershipExtractor.Extract(
        sfxEntries, groupFileReader, schemaProvider ?? new NullSchemaProvider());
    baseline = baseline with { GroupMemberships = groupMemberships };
    Console.WriteLine($"Group memberships: {groupMemberships.Count} group(s)");

    // ── File type registry ────────────────────────────────────────────────────

    var fileTypeMapBuilder =
        ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
    if (schemaProvider is not null)
        foreach (var def in schemaProvider.AllMetafiles)
        {
            if (def.MetafileType == MetafileType.Special) continue;

            if (def.MetafileType == MetafileType.FileRegistry)
            {
                var enginePath2 = def.Path.ToUpperInvariant().Replace('/', '\\');
                using var stream = engine.GameRepository.TryOpenFile(enginePath2);
                if (stream is not null)
                    try
                    {
                        var xmlContent = await new StreamReader(stream).ReadToEndAsync();
                        var xdoc = XDocument.Parse(xmlContent);
                        foreach (var elem in xdoc.Descendants()
                                     .Where(e => e.Name.LocalName.Equals("File", StringComparison.OrdinalIgnoreCase)))
                        {
                            var filename = elem.Value.Trim();
                            if (string.IsNullOrEmpty(filename))
                                filename = elem.Attributes()
                                    .FirstOrDefault(a =>
                                        a.Name.LocalName.Equals("filename", StringComparison.OrdinalIgnoreCase))?.Value;
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
            else
            {
                fileTypeMapBuilder[def.Path] = [.. def.Types];
            }
        }

    baseline = baseline with { FileTypeMap = fileTypeMapBuilder.ToImmutable() };
    Console.WriteLine($"File type registry: {baseline.FileTypeMap.Count} file(s) registered");

    // ── Asset catalog (MEG + loose) ───────────────────────────────────────────

    var looseFileSystem = engine.GameRepository.PGFileSystem.UnderlyingFileSystem;

    Func<Stream, IReadOnlyList<string>> getBones = stream =>
    {
        using var model = aloFileService.LoadModel(stream);
        return model.Content.Bones.ToList();
    };

    Func<Stream, IEnumerable<string>> getMtdIcons = stream =>
    {
        try { return mtdFileService.Load(stream).Content.Select(e => e.FileName); }
        catch { return []; }
    };

    var (assetFiles, modelBones) = MegAssetCatalogBuilder.Build(
        megEntries, looseFileSystem, engine.GameRepository.Path,
        openMegEntry, getBones, getMtdIcons, assetLogger);

    baseline = baseline with { AssetFiles = assetFiles, ModelBones = modelBones };
    Console.WriteLine($"Asset files: {baseline.AssetFiles.Count} asset file(s) (MEG + loose)");
    Console.WriteLine($"Model bones: {baseline.ModelBones.Count} model(s) with bone data");

    // ── Serialize ─────────────────────────────────────────────────────────────

    var data = BaselineSerializer.Serialize(baseline);

    var outputDir = Path.GetDirectoryName(outputFile);
    if (!string.IsNullOrEmpty(outputDir))
        Directory.CreateDirectory(outputDir);

    await File.WriteAllBytesAsync(outputFile, data);
    Console.WriteLine($"Written: {outputFile} ({data.Length:N0} bytes)");

    var manifestFile = Path.ChangeExtension(outputFile, ".manifest.json");
    await File.WriteAllTextAsync(manifestFile,
        $$"""{ "version": 1, "hash": "{{Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()}}" }""");
    Console.WriteLine($"Manifest: {manifestFile}");

    return 0;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static IReadOnlyList<string> BuildOrderedMegPaths(string? eawLayerPath, string enginePath)
{
    var result = new List<string>();

    if (eawLayerPath is not null)
    {
        var eawMegs = Directory.GetFiles(eawLayerPath, "*.meg", SearchOption.AllDirectories);
        result.AddRange(MegLoadOrderResolver.Resolve(eawMegs, eawLayerPath));
    }

    var engineMegs = Directory.GetFiles(enginePath, "*.meg", SearchOption.AllDirectories);
    result.AddRange(MegLoadOrderResolver.Resolve(engineMegs, enginePath));

    return result;
}

// Stored in BaselineIndex.SourceManifestHash as informational metadata only.
// The LSP server does not validate this hash at runtime; no game-install-path config exists.
// Intended for future version-mismatch tooling.
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

    public XmlTagDefinition? GetTag(string t) => null;
    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string t) => [];
    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string t) => [];
    public EnumDefinition? GetEnum(string e) => null;
    public GameObjectTypeDefinition? GetObjectType(string t) => null;
}
