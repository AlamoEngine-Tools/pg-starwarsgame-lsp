// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Core.Xml;
using PG.StarWarsGame.LSP.Lua.Schema;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Indexing;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Story.Discovery;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     The pure, pgproj-driven indexing work split out of the old <c>WorkspaceScanner</c>:
///     registers file types from metafiles, bulk-indexes the project's declared XML and script
///     directories, and publishes the asset and model-bone catalogs. Contains no waiting, no
///     events, no buffering, and no heuristic fallback - the <see cref="StartupPipeline" />
///     drives each step in a fixed order.
/// </summary>
public sealed class WorkspaceIndexer : IWorkspaceIndexer
{
    private readonly IModelBoneCatalogCache _boneCache;
    private readonly ILspConfigurationProvider _configProvider;
    private readonly DocumentIndexer _documents;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<WorkspaceIndexer> _logger;
    private readonly IMegEntryReader _megEntries;
    private readonly MegArchiveLoadOrder _megLoadOrder;
    private readonly MetafileRegistrar _metafiles;
    private readonly ISchemaProvider _schema;

    public WorkspaceIndexer(IFileHelper fileHelper, IGameIndexService indexService, ISchemaProvider schema,
        MetafileRegistrar metafiles,
        DocumentIndexer documents,
        ILspConfigurationProvider configProvider,
        IMegEntryReader megEntries,
        MegArchiveLoadOrder megLoadOrder,
        IModelBoneCatalogCache boneCache,
        ILogger<WorkspaceIndexer> logger)
    {
        _boneCache = boneCache;
        _fileHelper = fileHelper;
        _indexService = indexService;
        _schema = schema;
        _metafiles = metafiles;
        _documents = documents;
        _configProvider = configProvider;
        _megEntries = megEntries;
        _megLoadOrder = megLoadOrder;
        _logger = logger;
    }

    /// <summary>The metafile stage, <see cref="MetafileRegistrar" />, over the baseline loaded now.</summary>
    public void PreScanMetafiles(WorkspaceConfiguration config, IReadOnlyList<string> roots)
    {
        _metafiles.Register(config, roots, _indexService.Current.Baseline);
    }

    /// <inheritdoc />
    public IndexCacheStats? LastIndexCache => _documents.LastIndexCache;

    /// <inheritdoc />
    public BoneCatalogStats? LastBoneCatalog { get; private set; }

    /// <summary>The document stage, <see cref="DocumentIndexer" />.</summary>
    public Task<int> IndexDocumentsAsync(WorkspaceConfiguration config, CancellationToken ct,
        Action<int, int>? progress = null)
    {
        return _documents.IndexAsync(config, ct, progress);
    }

    /// <summary>
    ///     Collects the workspace's asset files (textures, models, audio, maps) - loose under its
    ///     asset roots AND packed into the archives the game loads from its project folders -
    ///     normalises them game-relative, unions with the baseline catalog, and publishes the merged
    ///     <see cref="IAssetFileIndex" /> on the GameIndex.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The archives are not optional. A mod ships its assets packed, so globbing loose files
    ///         alone reported every reference into a mod's own archives as a missing file, and the
    ///         shipped game escaped only because its baseline was built from its archives.
    ///     </para>
    ///     <para>
    ///         Which archives is the game's rule, <see cref="MegArchiveLoadOrder" />: the ones its
    ///         <c>Data\MegaFiles.xml</c> names plus the patch and sound archives, each name taken from
    ///         the first project folder that has it. A mod keeps them in <c>Data\</c>, beside its art
    ///         and audio roots, so a search of those roots found none of them. Archives that resolve to
    ///         the game folders are left to the baseline.
    ///     </para>
    /// </remarks>
    public void ApplyAssetCatalog(WorkspaceConfiguration config)
    {
        var baseline = _indexService.Current.Baseline.AssetFiles;
        var packed = 0;

        // Scripts are declared apart from the art and audio, as script roots; they are catalogued
        // with the rest so a Lua_Script resolves against loose, packed and shipped files alike.
        // The catalogue's own predicate rather than a third copy of its list: the workspace's
        // loose `.ala` files are exactly what a model preview needs enumerated, and a list that
        // drifts from the catalogue's leaves them out silently.
        var workspace = config.AssetRoots.Concat(config.ScriptRoots)
            .Where(_fileHelper.FileSystem.Directory.Exists)
            .SelectMany(root => _fileHelper.FileSystem.Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => MegAssetCatalogBuilder.IsAssetExtension(
                    _fileHelper.FileSystem.Path.GetExtension(file)))
                .Select(file => GameRelativePath(file, root)))
            .ToList();

        var archives = _megLoadOrder.SearchOrder(MegArchiveRoots.For(config, _configProvider.Current));
        foreach (var archive in archives.Where(a => a.Source == MegArchiveSource.Mod))
        foreach (var entry in _megEntries.EnumerateEntryPaths(archive.Path))
        {
            // Through the baseline builder's own rules, so a packed asset is spelled the same
            // whether it reached the catalog from the shipped game or from the workspace.
            var normalized = MegAssetCatalogBuilder.ApplySfxConventions(
                MegAssetCatalogBuilder.NormalizeMegPath(entry),
                _fileHelper.FileSystem.Path.GetFileName(archive.Path));
            if (!MegAssetCatalogBuilder.IsAssetExtension(Path.GetExtension(normalized)))
                continue;
            workspace.Add(normalized);
            packed++;
        }

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(
                "Asset catalog: {Workspace} workspace asset(s) ({Packed} from MEG archives) merged with {Baseline} baseline asset(s)",
                workspace.Count, packed, baseline.Count);

        _indexService.ApplyAssetFiles(MergedAssetFileIndex.Merge(baseline, workspace));
    }

    /// <summary>
    ///     Extracts bone names from loose workspace .alo models, unions them with the baseline bone
    ///     catalog (workspace models override shipped ones of the same filename), and publishes the
    ///     merged map on the GameIndex for boneName completion.
    /// </summary>
    public void ApplyModelBoneCatalog(WorkspaceConfiguration config)
    {
        var baselineBones = _indexService.Current.Baseline.ModelBones;
        var baselineTextures = _indexService.Current.Baseline.ModelTextures;

        var mergedBones = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (path, bones) in baselineBones)
            mergedBones[path] = bones;

        var mergedTextures = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (path, textures) in baselineTextures)
            mergedTextures[path] = textures;

        // Which .alo files are particle systems (#270), the workspace's entry deciding for its name.
        var particleSystems = _indexService.Current.Baseline.ParticleSystems.ToBuilder();
        // Each model's proxy names (#270), the workspace's model replacing the shipped one's.
        var proxies = _indexService.Current.Baseline.ModelProxies.ToBuilder();

        var workspaceCount = 0;
        var reusedLayers = 0;
        var layerCount = 0;

        // Rank ASCENDING - dependencies first, root project last - so a leaf's own model of the
        // same filename overwrites the dependency's, matching every other layered lookup. The old
        // flat config.AssetRoots union was already deps-first; this preserves that order while
        // making each layer separately cacheable.
        foreach (var layer in LayersForBones(config))
        {
            var (models, reused) = ModelsForLayer(layer);
            layerCount++;
            if (reused) reusedLayers++;

            foreach (var (path, entry) in models)
            {
                mergedBones[path] = [.. entry.Bones];
                // An empty texture list is recorded on purpose: it is the answer "this model names
                // none", and it is what keeps the validator from reopening the file to find out.
                mergedTextures[path] = [.. entry.Textures];
                if (entry.ParticleSystem) particleSystems.Add(path);
                else particleSystems.Remove(path);
                proxies[path] = [.. entry.Proxies];
                workspaceCount++;
            }
        }

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(
                "Model catalog: {Workspace} workspace model(s) merged with {Baseline} baseline "
                + "model(s); {Reused} layer(s) reused a snapshot; {Textures} model(s) with texture data",
                workspaceCount, baselineBones.Count, reusedLayers, mergedTextures.Count);
        LastBoneCatalog = new BoneCatalogStats(reusedLayers, layerCount);

        _indexService.ApplyModelBones(mergedBones.ToImmutable());
        _indexService.ApplyModelTextures(mergedTextures.ToImmutable());
        _indexService.ApplyParticleSystems(particleSystems.ToImmutable());
        _indexService.ApplyModelProxies(proxies.ToImmutable());
    }


    /// <summary>
    ///     Scans the workspace XML directories for every <see cref="EnumKind.DynamicXml" /> enum file
    ///     declared in the schema, parses it with <see cref="DynamicEnumExtractor" />, and publishes
    ///     the merged result as <see cref="IGameIndexService.ApplyWorkspaceDynamicEnumValues" />.
    ///     Only files that actually exist in the workspace are included; missing files are silently
    ///     skipped so dependency-only enums degrade gracefully to baseline-only validation.
    /// </summary>
    public void ApplyDynamicEnumCatalog(IReadOnlyList<string> xmlRoots)
    {
        var sep = _fileHelper.FileSystem.Path.DirectorySeparatorChar;

        // Filename-only search across every xml root, recursing into subdirectories. Collect the
        // wanted filenames up front and walk each root ONCE, instead of one recursive directory
        // walk per enum source file per root. Roots are iterated in reverse so the highest-rank
        // (root project) directory wins; config.XmlDirectories is deps-first / root-last (see
        // ModProjectResolver.Resolve).
        var wantedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var enumDef in _schema.AllEnums)
        {
            if (enumDef.Kind != EnumKind.DynamicXml || string.IsNullOrEmpty(enumDef.SourceFile))
                continue;
            var anchorIdx = enumDef.SourceFile.IndexOf('$');
            var filePath = anchorIdx >= 0 ? enumDef.SourceFile[..anchorIdx] : enumDef.SourceFile;
            wantedFileNames.Add(_fileHelper.FileSystem.Path.GetFileName(filePath.Replace('/', sep)));
        }

        var diskPathByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (wantedFileNames.Count > 0)
            foreach (var xmlRoot in xmlRoots.AsEnumerable().Reverse())
            {
                if (!_fileHelper.FileSystem.Directory.Exists(xmlRoot)) continue;
                foreach (var file in _fileHelper.FileSystem.Directory
                             .EnumerateFiles(xmlRoot, "*", SearchOption.AllDirectories))
                {
                    var name = _fileHelper.FileSystem.Path.GetFileName(file);
                    if (wantedFileNames.Contains(name))
                        diskPathByFileName.TryAdd(name, file);
                }
            }

        // Keyed by logical file path (anchor stripped). Caches (diskPath, content) so the file is
        // read once even when multiple enums share the same source file (e.g. gameconstants.xml).
        var resolvedFiles = new Dictionary<string, (string DiskPath, string Content)>(
            StringComparer.OrdinalIgnoreCase);

        var (workspace, _) = DynamicEnumExtractor.Extract(_schema, FileReader);

        // Build per-value file locations for both plain-file and $anchor format enums.
        var defsBuilder =
            ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, FileOrigin>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var enumDef in _schema.AllEnums)
        {
            if (enumDef.Kind != EnumKind.DynamicXml || string.IsNullOrEmpty(enumDef.SourceFile))
                continue;

            var sourceFile = enumDef.SourceFile;
            var anchorIdx = sourceFile.IndexOf('$');
            var filePath = anchorIdx >= 0 ? sourceFile[..anchorIdx] : sourceFile;

            if (!resolvedFiles.TryGetValue(filePath, out var resolved))
                continue;

            var fileUri = _fileHelper.PathToFileUri(resolved.DiskPath);

            var locations = anchorIdx >= 0
                ? DynamicEnumExtractor.ParseElementTextWithLocations(
                    resolved.Content, sourceFile[(anchorIdx + 1)..], fileUri)
                : DynamicEnumExtractor.ParseEnumDefinitionFileWithLocations(
                    resolved.Content, fileUri);

            if (locations.Count == 0) continue;

            var inner = ImmutableDictionary.CreateBuilder<string, FileOrigin>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, origin) in locations)
                inner[name] = origin;

            defsBuilder[enumDef.Name] = inner.ToImmutable();
        }

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(
                "Dynamic enum catalog: {EnumCount} enum(s) with {ValueCount} workspace value(s) applied",
                workspace.Count, workspace.Values.Sum(v => v.Length));

        _indexService.ApplyWorkspaceDynamicEnumValues(workspace);
        _indexService.ApplyWorkspaceEnumValueDefinitions(defsBuilder.ToImmutable());
        return;

        string? FileReader(string sourceFile)
        {
            var filePath = sourceFile.Contains('$') ? sourceFile[..sourceFile.IndexOf('$')] : sourceFile;

            if (resolvedFiles.TryGetValue(filePath, out var cached))
                return cached.Content;

            var fileName = _fileHelper.FileSystem.Path.GetFileName(filePath.Replace('/', sep));
            if (!diskPathByFileName.TryGetValue(fileName, out var match))
                return null;

            try
            {
                var content = _fileHelper.FileSystem.File.ReadAllText(match);
                resolvedFiles[filePath] = (match, content);
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read enum file '{Path}'", match);
                return null;
            }
        }
    }

    /// <summary>
    ///     Where an asset sits in the GAME tree, which is the only spelling the catalog can match on.
    /// </summary>
    /// <remarks>
    ///     A .pgproj declares its asset roots as <c>data/audio</c>, <c>data/art</c> and so on, so
    ///     taking the path relative to the root that matched threw the <c>data/audio/</c> prefix
    ///     away and stored <c>announcer/foo.wav</c>. The baseline stores <c>data/...</c>, and a
    ///     reference is written game-relative too - EaWX writes
    ///     <c>Data\Audio\Announcer\foo.wav</c> - so the two halves of the merged catalog never met.
    ///     Anchoring on the LAST <c>data</c> segment is what the engine's own layering does: a mod
    ///     root holds a <c>Data</c> directory and everything is addressed from inside it. With no
    ///     such segment the root-relative path stands, so a root outside a game tree is not guessed
    ///     at.
    /// </remarks>
    private string GameRelativePath(string absoluteFile, string root)
    {
        var normalized = _fileHelper.NormalizeGamePath(absoluteFile);
        var segments = normalized.Split('/');
        var data = Array.LastIndexOf(segments, "data");
        if (data >= 0 && data < segments.Length - 1)
            return string.Join('/', segments[data..]);

        return _fileHelper.NormalizeGamePath(
            _fileHelper.FileSystem.Path.GetRelativePath(root, absoluteFile));
    }

    /// <summary>
    ///     The layers to extract bones from, dependencies first. Falls back to one synthetic layer
    ///     over the flattened asset roots when the workspace resolved no projects (a heuristic
    ///     scan), which cannot be cached because there is no <c>.pgproj</c> to store it beside.
    /// </summary>
    private static ProjectLayer[] LayersForBones(WorkspaceConfiguration config)
    {
        return config.Layers.Count == 0
            ? [new ProjectLayer(0, "(workspace)", [], [], [], config.AssetRoots, null)]
            : [.. config.Layers.OrderBy(l => l.Rank)];
    }

    /// <summary>
    ///     One layer's models, from its persisted snapshot when the files on disk are unchanged,
    ///     otherwise by extracting and writing a fresh snapshot.
    /// </summary>
    private (IReadOnlyDictionary<string, ModelCatalogEntry> Models, bool Reused) ModelsForLayer(
        ProjectLayer layer)
    {
        var roots = layer.AssetRoots;
        if (roots.Count == 0)
            return (new Dictionary<string, ModelCatalogEntry>(), false);

        // No pgproj means no place to keep a snapshot beside - extract every time, as before.
        if (layer.ProjectPath is not { } pgprojPath)
            return (ExtractModels(roots), false);

        var fingerprint = ModelBoneFingerprint.Compute(_fileHelper, roots);
        if (_boneCache.TryLoad(pgprojPath, fingerprint) is { } cached)
            return (cached, true);

        var extracted = ExtractModels(roots);
        _boneCache.Save(pgprojPath, fingerprint, extracted);
        return (extracted, false);
    }

    /// <summary>
    ///     Bones and textures for every model under <paramref name="roots" />. A model that yields
    ///     no bones is still recorded when it yields textures, and vice versa - the two reads fail
    ///     independently, and a model whose skeleton will not load still has readable textures.
    /// </summary>
    private Dictionary<string, ModelCatalogEntry> ExtractModels(IReadOnlyList<string> roots)
    {
        var models = new Dictionary<string, ModelCatalogEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (!_fileHelper.FileSystem.Directory.Exists(root)) continue;

            foreach (var (path, names) in BoneNameExtractor.Extract(_fileHelper.FileSystem, root))
                models[path] = (models.GetValueOrDefault(path) ?? ModelCatalogEntry.Empty) with { Bones = names };

            foreach (var (path, scan) in BoneNameExtractor.ExtractTextures(_fileHelper.FileSystem, root))
                models[path] = (models.GetValueOrDefault(path) ?? ModelCatalogEntry.Empty) with
                {
                    Textures = [.. scan.Textures],
                    ParticleSystem = scan.Kind == AloFileKind.Particle,
                    Proxies = [.. scan.Proxies]
                };
        }

        return models;
    }

    public static bool IsAssetFile(string path)
    {
        return MegAssetCatalogBuilder.IsAssetExtension(Path.GetExtension(path));
    }
}
