// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     The pure, pgproj-driven indexing work split out of the old <c>WorkspaceScanner</c>:
///     registers file types from metafiles, bulk-indexes the project's declared XML and script
///     directories, and publishes the asset and model-bone catalogs. Contains no waiting, no
///     events, no buffering, and no heuristic fallback — the <see cref="StartupPipeline" />
///     drives each step in a fixed order.
/// </summary>
public sealed class WorkspaceIndexer : IWorkspaceIndexer
{
    // Asset extensions covered by the shared asset-file catalog. Mirrors the set enumerated by
    // the BaselineBuilder's AssetFileEnumerator.
    private static readonly HashSet<string> AssetExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".tga", ".dds", ".alo", ".wav", ".mp3", ".ted" };

    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IFileTypeRegistry _fileTypeRegistry;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<WorkspaceIndexer> _logger;
    private readonly IEnumerable<IGameDocumentParser> _parsers;
    private readonly ISchemaProvider _schema;

    public WorkspaceIndexer(IFileHelper fileHelper, IEnumerable<IGameDocumentParser> parsers,
        IGameIndexService indexService, IFileTypeRegistry fileTypeRegistry, ISchemaProvider schema,
        IEaWXmlContext eaWXmlContext, ILogger<WorkspaceIndexer> logger)
    {
        _fileHelper = fileHelper;
        _parsers = parsers;
        _indexService = indexService;
        _fileTypeRegistry = fileTypeRegistry;
        _schema = schema;
        _eaWXmlContext = eaWXmlContext;
        _logger = logger;
    }

    /// <summary>
    ///     Seeds the EaW XML context with the project's declared XML directories, then walks the
    ///     schema's metafile definitions to register concrete file → type mappings (from the
    ///     metafile on disk, or the shipped baseline when absent).
    /// </summary>
    public void PreScanMetafiles(WorkspaceConfiguration config, IReadOnlyList<string> roots)
    {
        _logger.LogDebug("PreScanMetafiles started");
        if (config.XmlDirectories.Count > 0)
            _eaWXmlContext.SetDirectories(config.XmlDirectories);

        var xmlRoots = config.XmlDirectories.ToList();
        var baseline = _indexService.Current.Baseline;

        foreach (var def in _schema.AllMetafiles)
        {
            if (def.MetafileType == MetafileType.Special) continue;

            // A metafile (or DirectContent file) may be shipped by any layer — the mod or a
            // dependency — so look under the workspace roots AND every declared xml root, and use
            // every copy found. Only fall back to the baseline when no copy exists anywhere.
            var copies = LocateInLayers(def.Path, roots, xmlRoots);
            if (copies.Count == 0)
            {
                FallbackFromBaseline(baseline, def, xmlRoots);
                continue;
            }

            foreach (var path in copies)
            {
                _eaWXmlContext.AddDirectory(_fileHelper.FileSystem.Path.GetDirectoryName(path)!);
                if (def.MetafileType == MetafileType.FileRegistry)
                    RegisterFromMetafile(path, def, xmlRoots);
                else // DirectContent: the file itself carries the type
                    _fileTypeRegistry.RegisterFile(_fileHelper.PathToFileUri(path), def.Types.ToImmutableArray());
            }
        }
    }

    // Locates every existing copy of a metafile/content file (identified by its game-relative
    // <paramref name="defPath" />) across the workspace roots and every declared xml root, so a
    // metafile shipped by a dependency is found rather than silently missed.
    private IReadOnlyList<string> LocateInLayers(
        string defPath, IReadOnlyList<string> roots, IReadOnlyList<string> xmlRoots)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void TryAdd(string? path)
        {
            if (string.IsNullOrEmpty(path) || !_fileHelper.FileSystem.File.Exists(path)) return;
            if (seen.Add(_fileHelper.NormalizeUri(path)))
                found.Add(path);
        }

        TryAdd(_fileHelper.FindInWorkspace(roots.ToList(), defPath));

        // Metafiles live directly in the xml directory, keyed by the def's filename.
        var fileName = _fileHelper.FileSystem.Path.GetFileName(defPath);
        foreach (var xmlRoot in xmlRoots)
            TryAdd(_fileHelper.FileSystem.Path.Combine(xmlRoot, fileName));

        return found;
    }

    /// <summary>
    ///     Bulk-indexes every parseable XML file under the declared XML directories (gated by
    ///     <see cref="IEaWXmlContext.IsEaWXmlFile" />) and every parseable script file under the
    ///     declared script roots, in a single <see cref="IGameIndexService.BeginBulkUpdate" />.
    ///     Returns the number of files indexed.
    /// </summary>
    public async Task<int> IndexDocumentsAsync(WorkspaceConfiguration config, CancellationToken ct,
        Action<int, int>? progress = null)
    {
        var files = EnumerateFiles(config.XmlDirectories)
            .Where(f =>
            {
                var ext = _fileHelper.FileSystem.Path.GetExtension(f);
                if (!ext.Equals(".xml", StringComparison.OrdinalIgnoreCase)) return false;
                if (!_parsers.Any(p => p.CanParse(ext))) return false;
                // XML files are gated to the declared EaW directories so unrelated XML
                // (project settings, build files) is never indexed.
                return _eaWXmlContext.IsEaWXmlFile(_fileHelper.PathToFileUri(f));
            })
            .Concat(EnumerateFiles(config.ScriptRoots)
                .Where(f =>
                {
                    var ext = _fileHelper.FileSystem.Path.GetExtension(f);
                    if (ext.Equals(".xml", StringComparison.OrdinalIgnoreCase)) return false;
                    // Non-XML parseable files (e.g. .lua) have no directory gate — any
                    // parseable file under the script roots is a game file.
                    return _parsers.Any(p => p.CanParse(ext));
                }))
            .Distinct()
            .ToList();

        _logger.LogInformation("WorkspaceIndexer: {Count} parseable file(s) found", files.Count);

        var indexed = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct };
        using (_indexService.BeginBulkUpdate())
        {
            await Parallel.ForEachAsync(files, options, async (file, token) =>
            {
                var text = await _fileHelper.FileSystem.File.ReadAllTextAsync(file, token);
                var uri = _fileHelper.PathToFileUri(file);
                await _indexService.UpdateDocumentAsync(uri, text, 0, token);
                var done = Interlocked.Increment(ref indexed);
                progress?.Invoke(done, files.Count);
            });
        } // fires one IndexChanged with the complete final state

        _logger.LogInformation("WorkspaceIndexer: bulk-index complete, {Indexed} file(s)", indexed);
        return indexed;
    }

    /// <summary>
    ///     Globs loose asset files (textures, models, audio, maps) under the given roots,
    ///     normalises them relative to their root, unions with the baseline catalog, and publishes
    ///     the merged <see cref="IAssetFileIndex" /> on the GameIndex.
    /// </summary>
    public void ApplyAssetCatalog(IReadOnlyList<string> roots)
    {
        var baseline = _indexService.Current.Baseline.AssetFiles;
        var workspace = new List<string>();

        foreach (var root in roots)
        {
            if (!_fileHelper.FileSystem.Directory.Exists(root)) continue;
            foreach (var file in _fileHelper.FileSystem.Directory
                         .EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!AssetExtensions.Contains(_fileHelper.FileSystem.Path.GetExtension(file)))
                    continue;
                var relative = _fileHelper.FileSystem.Path.GetRelativePath(root, file);
                workspace.Add(_fileHelper.NormalizeGamePath(relative));
            }
        }

        _logger.LogInformation(
            "Asset catalog: {Workspace} workspace asset(s) merged with {Baseline} baseline asset(s)",
            workspace.Count, baseline.Count);

        _indexService.ApplyAssetFiles(MergedAssetFileIndex.Merge(baseline, workspace));
    }

    /// <summary>
    ///     Extracts bone names from loose workspace .alo models, unions them with the baseline bone
    ///     catalog (workspace models override shipped ones at the same path), and publishes the
    ///     merged map on the GameIndex for boneName completion.
    /// </summary>
    public void ApplyModelBoneCatalog(IReadOnlyList<string> roots)
    {
        var baseline = _indexService.Current.Baseline.ModelBones;
        var merged = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (path, bones) in baseline)
            merged[path] = bones;

        var workspaceCount = 0;
        foreach (var root in roots)
        {
            if (!_fileHelper.FileSystem.Directory.Exists(root)) continue;
            var bonesByModel = BoneNameExtractor.Extract(_fileHelper.FileSystem, root);
            foreach (var (path, bones) in bonesByModel)
            {
                merged[path] = bones.ToImmutableArray();
                workspaceCount++;
            }
        }

        _logger.LogInformation(
            "Model bone catalog: {Workspace} workspace model(s) merged with {Baseline} baseline model(s)",
            workspaceCount, baseline.Count);

        _indexService.ApplyModelBones(merged.ToImmutable());
    }

    public static bool IsAssetFile(string path)
    {
        return AssetExtensions.Contains(Path.GetExtension(path));
    }

    private IEnumerable<string> EnumerateFiles(IEnumerable<string> roots)
    {
        return roots
            .Where(_fileHelper.FileSystem.Directory.Exists)
            .SelectMany(folder =>
                _fileHelper.FileSystem.Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories));
    }

    private void RegisterFromMetafile(string metafilePath, MetafileDefinition def, IReadOnlyList<string> xmlRoots)
    {
        string xmlContent;
        try
        {
            xmlContent = _fileHelper.FileSystem.File.ReadAllText(metafilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not read metafile '{Path}': {Message}", metafilePath, ex.Message);
            return;
        }

        XDocument xdoc;
        try
        {
            xdoc = XDocument.Parse(xmlContent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not parse metafile '{Path}': {Message}", metafilePath, ex.Message);
            return;
        }

        var sep = _fileHelper.FileSystem.Path.DirectorySeparatorChar;
        var types = def.Types.ToImmutableArray();
        foreach (var elem in xdoc.Descendants()
                     .Where(e => e.Name.LocalName.Equals("File", StringComparison.OrdinalIgnoreCase)))
        {
            var filename = elem.Value.Trim();
            if (string.IsNullOrEmpty(filename)) continue;

            // Metafile entries are paths relative to the XML directory, but may be a bare filename,
            // include subdirectories ("Units\Foo.xml"), or be fully qualified ("Data\XML\Foo.xml").
            // Normalise to an xml-dir-relative path (subdirectories PRESERVED) and resolve it against
            // EVERY xml root, so a mod's registry can type files that live in a dependency project and
            // files in subdirectories are no longer dropped.
            var rel = ToXmlRelativePath(_fileHelper.NormalizeGamePath(filename)).Replace('/', sep);
            foreach (var xmlRoot in xmlRoots)
                _fileTypeRegistry.RegisterFile(
                    _fileHelper.PathToFileUri(_fileHelper.FileSystem.Path.Combine(xmlRoot, rel)), types);
        }
    }

    // Falls back to the shipped baseline's file→type map when a metafile is absent from the workspace.
    // Preserves subdirectories and resolves each key against every xml root (mirrors RegisterFromMetafile).
    private void FallbackFromBaseline(BaselineIndex baseline, MetafileDefinition def,
        IReadOnlyList<string> xmlRoots)
    {
        if (xmlRoots.Count == 0) return;

        var sep = _fileHelper.FileSystem.Path.DirectorySeparatorChar;
        foreach (var (relativePath, types) in baseline.FileTypeMap)
        {
            if (!types.Any(t => def.Types.Contains(t, StringComparer.OrdinalIgnoreCase))) continue;

            var rel = ToXmlRelativePath(relativePath).Replace('/', sep);
            foreach (var xmlRoot in xmlRoots)
                _fileTypeRegistry.RegisterFile(
                    _fileHelper.PathToFileUri(_fileHelper.FileSystem.Path.Combine(xmlRoot, rel)), types);
        }
    }

    // Metafile entries and baseline keys come in two forms: xml-dir-relative ("units/foo.xml") or
    // game-root-relative ("data/xml/units/foo.xml"). Strip the leading data/xml prefix when present so
    // both collapse to an xml-dir-relative path, with any subdirectories preserved.
    private static string ToXmlRelativePath(string normalizedGamePath)
    {
        const string xmlPrefix = "data/xml/";
        return normalizedGamePath.StartsWith(xmlPrefix, StringComparison.Ordinal)
            ? normalizedGamePath[xmlPrefix.Length..]
            : normalizedGamePath;
    }
}