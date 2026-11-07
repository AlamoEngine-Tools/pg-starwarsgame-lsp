// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Assets;

namespace PG.StarWarsGame.LSP.Server.Indexing;

/// <summary>
///     The asset catalog: every asset file the workspace's projects ship, loose or packed, merged
///     game-relative over the baseline's and published on the game index.
/// </summary>
public sealed class AssetCatalogBuilder
{
    private readonly ILspConfigurationProvider _configProvider;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<AssetCatalogBuilder> _logger;
    private readonly IMegEntryReader _megEntries;
    private readonly MegArchiveLoadOrder _megLoadOrder;

    public AssetCatalogBuilder(IFileHelper fileHelper, IGameIndexService indexService,
        ILspConfigurationProvider configProvider, IMegEntryReader megEntries, MegArchiveLoadOrder megLoadOrder,
        ILogger<AssetCatalogBuilder> logger)
    {
        _fileHelper = fileHelper;
        _indexService = indexService;
        _configProvider = configProvider;
        _megEntries = megEntries;
        _megLoadOrder = megLoadOrder;
        _logger = logger;
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
    public void Apply(WorkspaceConfiguration config)
    {
        var baseline = _indexService.Current.Baseline.AssetFiles;
        var packed = 0;

        // Scripts are declared apart from the art and audio, as script roots; they are catalogued
        // with the rest so a Lua_Script resolves against loose, packed and shipped files alike.
        // The catalogue's own predicate rather than a third copy of its list: the workspace's
        // loose `.ala` files are exactly what a model preview needs enumerated, and a list that
        // drifts from the catalogue's leaves them out silently.
        //
        // Each root with the project it belongs to, so the Mod Browser can name where an asset comes
        // from; a root no project declares (a workspace without project files) carries none.
        var layered = config.Layers
            .SelectMany(layer => layer.AssetRoots.Concat(layer.ScriptRoots)
                .Select(root => (Root: root, Rank: (int?)layer.Rank)))
            .ToList();
        var undeclared = config.AssetRoots.Concat(config.ScriptRoots)
            .Where(root => !layered.Any(l => string.Equals(l.Root, root, StringComparison.OrdinalIgnoreCase)))
            .Select(root => (Root: root, Rank: (int?)null));
        var workspace = layered.Concat(undeclared)
            .Where(r => _fileHelper.FileSystem.Directory.Exists(r.Root))
            .SelectMany(r => _fileHelper.FileSystem.Directory
                .EnumerateFiles(r.Root, "*", SearchOption.AllDirectories)
                .Where(file => MegAssetCatalogBuilder.IsAssetExtension(
                    _fileHelper.FileSystem.Path.GetExtension(file)))
                .Select(file => (Path: GameRelativePath(file, r.Root), r.Rank)))
            .ToList();

        var archives = _megLoadOrder.SearchOrder(MegArchiveRoots.For(config, _configProvider.Current));
        foreach (var archive in archives.Where(a => a.Source == MegArchiveSource.Mod))
        {
            var rank = RankOfFolderHolding(config, archive.Path);
            foreach (var entry in _megEntries.EnumerateEntryPaths(archive.Path))
            {
                // Through the baseline builder's own rules, so a packed asset is spelled the same
                // whether it reached the catalog from the shipped game or from the workspace.
                var normalized = MegAssetCatalogBuilder.ApplySfxConventions(
                    MegAssetCatalogBuilder.NormalizeMegPath(entry),
                    _fileHelper.FileSystem.Path.GetFileName(archive.Path));
                if (!MegAssetCatalogBuilder.IsAssetExtension(Path.GetExtension(normalized)))
                    continue;
                workspace.Add((normalized, rank));
                packed++;
            }
        }

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(
                "Asset catalog: {Workspace} workspace asset(s) ({Packed} from MEG archives) merged with {Baseline} baseline asset(s)",
                workspace.Count, packed, baseline.Count);

        _indexService.ApplyAssetFiles(MergedAssetFileIndex.MergeLayered(baseline, workspace));
    }

    /// <summary>The rank of the project whose folder holds <paramref name="path" />, the deepest one first.</summary>
    private static int? RankOfFolderHolding(WorkspaceConfiguration config, string path)
    {
        var file = GamePaths.Forward(path);
        return config.Layers
            .Where(l => l.ProjectDirectory is { } dir &&
                        file.StartsWith(dir.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.ProjectDirectory!.Length)
            .Select(l => (int?)l.Rank)
            .FirstOrDefault();
    }

    /// <summary>Whether a watched file's change touches this catalog.</summary>
    public static bool IsAssetFile(string path)
    {
        return MegAssetCatalogBuilder.IsAssetExtension(Path.GetExtension(path));
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
}