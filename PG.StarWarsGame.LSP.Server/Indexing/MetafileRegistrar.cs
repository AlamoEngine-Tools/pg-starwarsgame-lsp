// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Core.Xml;
using PG.StarWarsGame.LSP.Story.Discovery;

namespace PG.StarWarsGame.LSP.Server.Indexing;

/// <summary>
///     The first indexing stage: seeds the EaW XML context with the project's declared XML
///     directories, then walks the schema's metafile definitions to register concrete file -> type
///     mappings in the <see cref="IFileTypeRegistry" /> - from the metafile on disk, or the shipped
///     baseline when no layer ships one - and follows the campaign registry's story chain. Each
///     listed file is typed at the one copy the game opens, and each file registry's entries are
///     recorded in load order in the <see cref="IRegistryWalkStore" />.
/// </summary>
public sealed class MetafileRegistrar
{
    private static readonly ImmutableArray<string> StoryPlotManifestTypes = ["StoryPlotManifest"];
    private static readonly ImmutableArray<string> StoryParserTypes = ["StoryParser"];

    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IFileTypeRegistry _fileTypeRegistry;
    private readonly ILogger<MetafileRegistrar> _logger;
    private readonly ISchemaProvider _schema;
    private readonly IStoryChainProblemStore _storyChainProblems;
    private readonly IRegistryWalkStore _walks;

    public MetafileRegistrar(IFileHelper fileHelper, IFileTypeRegistry fileTypeRegistry, ISchemaProvider schema,
        IEaWXmlContext eaWXmlContext, IStoryChainProblemStore storyChainProblems, IRegistryWalkStore walks,
        ILogger<MetafileRegistrar> logger)
    {
        _fileHelper = fileHelper;
        _fileTypeRegistry = fileTypeRegistry;
        _schema = schema;
        _eaWXmlContext = eaWXmlContext;
        _storyChainProblems = storyChainProblems;
        _walks = walks;
        _logger = logger;
    }

    /// <summary>
    ///     Registers every metafile's files for <paramref name="config" />, falling back to
    ///     <paramref name="baseline" /> for a metafile no layer ships, and replaces the registration,
    ///     the registry walks and the story chain's problems with this scan's.
    /// </summary>
    public void Register(WorkspaceConfiguration config, IReadOnlyList<string> roots, BaselineIndex baseline)
    {
        _logger.LogDebug("PreScanMetafiles started");
        if (config.XmlDirectories.Count > 0)
            _eaWXmlContext.SetDirectories(config.XmlDirectories);

        var leafLayer = config.Layers.Count > 0
            ? config.Layers.OrderByDescending(l => l.Rank).First()
            : null;
        _eaWXmlContext.SetLeafDirectories(leafLayer?.XmlDirectories ?? []);

        var xmlRoots = config.XmlDirectories.ToList();

        // Built aside and swapped in whole: adding to the live registry kept a copy typed after a
        // reload had shadowed it or its registry had dropped it.
        var typed = new Dictionary<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        var walks = new List<RegistryWalk>();
        var storyProblems = new List<StoryChainProblem>();
        foreach (var def in _schema.AllMetafiles)
        {
            if (def.MetafileType == MetafileType.Special)
            {
                ScanStoryChain(def, roots, xmlRoots, baseline, storyProblems, typed);
                continue;
            }

            // A metafile (or DirectContent file) may be shipped by any layer - the mod or a
            // dependency - so look under every declared xml root AND the workspace roots. Only fall
            // back to the baseline when no copy exists anywhere.
            var copies = LocateInLayers(def.Path, roots, xmlRoots);
            if (copies.Count == 0)
            {
                if (def.MetafileType == MetafileType.FileRegistry &&
                    baseline.RegistryLists.TryGetValue(GamePaths.Key(def.Path), out var shipped))
                    walks.Add(Walk(def, null, shipped, xmlRoots, typed));
                else
                    FallbackFromBaseline(baseline, def, xmlRoots, typed);
                continue;
            }

            // Every copy's directory holds EaW XML regardless of which registry wins - seeding the
            // context is about recognising files, not typing them.
            foreach (var path in copies)
                _eaWXmlContext.AddDirectory(_fileHelper.FileSystem.Path.GetDirectoryName(path)!);

            // ...but only the highest layer's copy is read: the engine takes the first file it
            // finds by name and ignores the rest, so a lower layer's entries are shadowed, not
            // merged. Reading them too would type files the game never loads.
            var winner = copies[0];
            if (def.MetafileType == MetafileType.FileRegistry)
            {
                // The registry is read by the game too, by a name compiled into it, so it is
                // registered under its own URI - with no types, because the schema does not model
                // its contents. Registering only the files it names reported the registry itself as
                // read by nothing.
                typed[_fileHelper.PathToFileUri(winner)] = [];
                if (ReadMetafile(winner) is { } listed)
                    walks.Add(Walk(def, _fileHelper.PathToFileUri(winner), listed, xmlRoots, typed));
            }
            else // DirectContent: the file itself carries the type
            {
                typed[_fileHelper.PathToFileUri(winner)] = [.. def.Types];
            }
        }

        _fileTypeRegistry.Replace(typed);
        _walks.Replace(walks);
        // Replace-all also clears stale problems when a rescan yields none.
        _storyChainProblems.Replace(storyProblems);
    }

    /// <summary>
    ///     Follows a <see cref="MetafileType.Special" /> definition (campaignfiles.xml) through the
    ///     campaign -> plot manifest -> story thread chain and types every discovered file
    ///     (<c>StoryPlotManifest</c> / <c>StoryParser</c>) at the copy the game opens. Chain problems
    ///     are collected for the diagnostics pipeline.
    /// </summary>
    private void ScanStoryChain(MetafileDefinition def, IReadOnlyList<string> roots,
        IReadOnlyList<string> xmlRoots, BaselineIndex baseline, List<StoryChainProblem> problems,
        Dictionary<string, ImmutableArray<string>> typed)
    {
        var copies = LocateInLayers(def.Path, roots, xmlRoots);

        // Opened by the game by its compiled-in name, like every other registry, whether or not
        // the chain behind it is followed here: registered under its own URI with no types. The
        // chain types the files it reaches, not this one.
        if (copies.Count > 0)
            typed[_fileHelper.PathToFileUri(copies[0])] = [];

        if (copies.Count == 0)
        {
            // No campaign registry anywhere - same degradation as file registries: the shipped
            // baseline's typed story files are projected onto the workspace's xml roots.
            FallbackFromBaseline(baseline, def, xmlRoots, typed);
            return;
        }

        // Highest layer only - the mod's campaignfiles.xml shadows a dependency's rather than
        // extending it, exactly as any other metafile. The winning registry can still name files
        // that live in a lower layer; the resolver searches every xml root for those.
        var winner = copies[0];
        StoryChainFile registry;
        try
        {
            registry = new StoryChainFile(
                _fileHelper.FileSystem.File.ReadAllText(winner), _fileHelper.NormalizeUri(winner));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read campaign registry '{Path}'", winner);
            return;
        }

        var resolver = new WorkspaceStoryChainFileResolver(_fileHelper, xmlRoots, baseline);
        var result = new StoryChainScanner(resolver).Scan(registry);

        foreach (var rel in result.ManifestFiles)
            TypeEntry(GamePaths.Key(rel), StoryPlotManifestTypes, xmlRoots, typed);
        foreach (var rel in result.ThreadFiles)
            TypeEntry(GamePaths.Key(rel), StoryParserTypes, xmlRoots, typed);
        problems.AddRange(result.Problems);

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(
                "Story chain: {Manifests} manifest(s), {Threads} thread file(s), {Lua} Lua script(s), {Problems} problem(s)",
                result.ManifestFiles.Count, result.ThreadFiles.Count, result.LuaScripts.Count,
                result.Problems.Count);
    }

    // Locates every existing copy of a metafile/content file (identified by its game-relative
    // <paramref name="defPath" />) across every declared xml root and the workspace roots, so a
    // metafile shipped by a dependency is found rather than silently missed.
    //
    // Ordered HIGHEST LAYER FIRST: the engine resolves a file by name, first-found-wins, and never
    // merges two copies of a registry - so copies[0] is the one the game actually reads and the
    // rest are shadowed. config.XmlDirectories arrives dependencies-first / root-project-last (see
    // ModProjectResolver.Resolve), hence the reversed walk; the workspace-root probe comes last as
    // a fallback for a metafile that sits outside every declared xml directory.
    private List<string> LocateInLayers(
        string defPath, IReadOnlyList<string> roots, IReadOnlyList<string> xmlRoots)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Metafiles live directly in the xml directory, keyed by the def's filename.
        var fileName = _fileHelper.FileSystem.Path.GetFileName(defPath);
        foreach (var xmlRoot in xmlRoots.AsEnumerable().Reverse())
            TryAdd(_fileHelper.FileSystem.Path.Combine(xmlRoot, fileName));

        TryAdd(_fileHelper.FindInWorkspace([.. roots], defPath));

        return found;

        void TryAdd(string? path)
        {
            if (string.IsNullOrEmpty(path) || !_fileHelper.FileSystem.File.Exists(path)) return;
            if (seen.Add(_fileHelper.NormalizeUri(path)))
                found.Add(path);
        }
    }

    private IReadOnlyList<string>? ReadMetafile(string metafilePath)
    {
        try
        {
            return MetafileReader.ReadFileKeys(_fileHelper.FileSystem.File.ReadAllText(metafilePath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read metafile '{Path}'", metafilePath);
            return null;
        }
    }

    // Registry entries are paths relative to the XML directory, but may be a bare filename, include
    // subdirectories ("Units\Foo.xml"), or be fully qualified ("Data\XML\Foo.xml"). Each is normalised
    // to an xml-dir-relative key (subdirectories PRESERVED) and typed at the copy the game opens, so a
    // mod's registry can type a file that lives in a dependency project.
    private RegistryWalk Walk(MetafileDefinition def, string? registryUri, IEnumerable<string> listed,
        IReadOnlyList<string> xmlRoots, Dictionary<string, ImmutableArray<string>> typed)
    {
        var types = def.Types.ToImmutableArray();
        var entries = listed
            .Select(entry => ToXmlRelativePath(GamePaths.Key(entry)))
            .Select(key => new RegistryWalkEntry(key, TypeEntry(key, types, xmlRoots, typed)))
            .ToImmutableArray();
        return new RegistryWalk(GamePaths.Key(def.Path), registryUri, entries);
    }

    // The game opens a listed file from the first layer that has it, highest first, and never reads
    // another copy - so only that copy is typed. With none in any layer, the leaf project's path is
    // typed instead: a copy created there wins, and the game opens the shipped one until then.
    // Returns the URI of the layer copy that wins, or null when no layer ships the file.
    private string? TypeEntry(string key, ImmutableArray<string> types, IReadOnlyList<string> xmlRoots,
        Dictionary<string, ImmutableArray<string>> typed)
    {
        if (xmlRoots.Count == 0) return null;

        string? winner = null;
        for (var i = xmlRoots.Count - 1; i >= 0 && winner is null; i--)
            winner = _fileHelper.FindInWorkspace([xmlRoots[i]], key);

        var sep = _fileHelper.FileSystem.Path.DirectorySeparatorChar;
        var target = winner ?? _fileHelper.FileSystem.Path.Combine(xmlRoots[^1], key.Replace('/', sep));
        typed[_fileHelper.PathToFileUri(target)] = types;
        return winner is null ? null : _fileHelper.PathToFileUri(winner);
    }

    // Falls back to the shipped baseline's file->type map when a metafile is absent from the workspace
    // and the baseline carries no ordered list for it (a baseline built before the lists).
    private void FallbackFromBaseline(BaselineIndex baseline, MetafileDefinition def,
        IReadOnlyList<string> xmlRoots, Dictionary<string, ImmutableArray<string>> typed)
    {
        foreach (var (relativePath, types) in baseline.FileTypeMap)
        {
            if (!types.Any(t => def.Types.Contains(t, StringComparer.OrdinalIgnoreCase))) continue;

            TypeEntry(ToXmlRelativePath(GamePaths.Key(relativePath)), types, xmlRoots, typed);
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