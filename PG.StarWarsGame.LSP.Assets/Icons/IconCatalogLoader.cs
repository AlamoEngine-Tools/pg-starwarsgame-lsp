// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.Files.MTD.Services;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Project;

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>
///     Assembles an <see cref="IconCatalog" /> for one project from what is actually on disk.
/// </summary>
/// <remarks>
///     Every layer is optional and every failure is survivable. A project with no mega texture of its
///     own, no source folders, or an unreadable atlas still gets a working catalog backed by the
///     baked baseline - previewing icons is a convenience, and no arrangement of missing art should
///     stop the editor from answering.
/// </remarks>
public static class IconCatalogLoader
{
    /// <param name="fileSystem">Filesystem the project lives on.</param>
    /// <param name="mtdFileService">
    ///     MTD reader. Must be backed by the same filesystem as <paramref name="fileSystem" />: it
    ///     opens the file itself rather than accepting a stream, because the underlying library
    ///     rejects streams that carry no path information.
    /// </param>
    /// <param name="rootPath">Project root that <paramref name="settings" />' paths are relative to.</param>
    /// <param name="settings">The project's icon configuration.</param>
    /// <param name="baseline">Icons baked into the baseline sidecar; may be empty.</param>
    /// <param name="logger">Optional; receives one line per layer that could not be read.</param>
    public static IconCatalog Load(
        IFileSystem fileSystem,
        IMtdService mtdFileService,
        string rootPath,
        IconProjectSettings settings,
        IconPack baseline,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Load(fileSystem, mtdFileService, [new IconLayer(rootPath, settings)], baseline, logger);
    }

    /// <summary>
    ///     The catalog for a LAYERED workspace, <paramref name="layers" /> highest precedence first.
    /// </summary>
    /// <remarks>
    ///     A leaf mod used to reach its dependency's icons through nothing at all: the catalog was
    ///     built from one root and the root project's settings, so a referenced project's mega
    ///     texture and its loose sources were invisible and its art fell through to the baked
    ///     baseline.
    ///     <para>
    ///         The two kinds layer differently, on purpose. A mega texture REPLACES rather than
    ///         merges, so the first layer that ships one wins outright and no later atlas is read -
    ///         which is what keeps a build pipeline that packs dependency icons into the leaf's
    ///         atlas authoritative. Loose sources are individual files, so they layer by name and
    ///         the leaf's copy wins.
    ///     </para>
    /// </remarks>
    public static IconCatalog Load(
        IFileSystem fileSystem,
        IMtdService mtdFileService,
        IReadOnlyList<IconLayer> layers,
        IconPack baseline,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(mtdFileService);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(baseline);

        logger ??= NullLogger.Instance;

        // First atlas found wins; the rest are not opened at all.
        var workspace = layers
            .Select(layer => LoadWorkspaceMegaTexture(
                fileSystem, mtdFileService, layer.RootPath, layer.Settings, logger))
            .FirstOrDefault(icons => icons is not null);

        // Every layer's loose roots, leaf first, so the scan's own first-wins rule gives the leaf
        // precedence without a second pass.
        var roots = layers
            .SelectMany(layer => layer.Settings.SourceRoots
                .Select(r => fileSystem.Path.Combine(layer.RootPath, r)))
            .ToList();
        var catalog = LooseIconCatalog.Scan(fileSystem, roots);
        var loose = LooseIconDecoder.DecodeAll(fileSystem, catalog, out var unsupported);

        foreach (var name in unsupported)
            logger.LogWarning(
                "Icon source '{Name}' could not be decoded; BMP sources are not supported and " +
                "corrupt images are skipped.", name);

        return new IconCatalog(workspace, loose, baseline.Icons);
    }


    /// <summary>
    ///     Returns the workspace's own icons, or <see langword="null" /> when it ships no mega
    ///     texture. The distinction matters: null lets the baked baseline answer, whereas an empty
    ///     dictionary would (correctly) suppress it.
    /// </summary>
    private static IReadOnlyDictionary<string, byte[]>? LoadWorkspaceMegaTexture(
        IFileSystem fileSystem,
        IMtdService mtdFileService,
        string rootPath,
        IconProjectSettings settings,
        ILogger logger)
    {
        var mtdPath = fileSystem.Path.Combine(rootPath, settings.MtdPath);
        var texturePath = fileSystem.Path.Combine(rootPath, settings.TexturePath);

        if (!fileSystem.File.Exists(mtdPath) || !fileSystem.File.Exists(texturePath))
            return null;

        try
        {
            var directory = mtdFileService.LoadFile(mtdPath).Content;
            using var texture = fileSystem.File.OpenRead(texturePath);
            return MegaTextureIconExtractor.ExtractAll(directory, texture);
        }
        catch (Exception ex)
        {
            // An unreadable atlas is reported, not thrown: treating it as "ships none" lets the
            // baseline answer, which is far more useful than refusing to preview anything.
            logger.LogWarning(ex,
                "Could not read the workspace mega texture at '{Path}'; falling back to the baked " +
                "base-game icons.", mtdPath);
            return null;
        }
    }
}