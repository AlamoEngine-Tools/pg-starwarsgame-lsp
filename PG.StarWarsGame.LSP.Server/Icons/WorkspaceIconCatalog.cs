// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Icons;

/// <summary>The icon catalog for whichever workspace is currently loaded.</summary>
public interface IWorkspaceIconCatalog
{
    /// <summary>
    ///     The catalog, or <see langword="null" /> when icons are unavailable for any reason - no
    ///     provider, no workspace root, or a catalog that would not build.
    /// </summary>
    Task<IconCatalog?> GetAsync(CancellationToken ct);
}

/// <summary>
///     Finds the current workspace root and asks <see cref="IIconCatalogProvider" /> for its catalog.
/// </summary>
/// <remarks>
///     <para>
///         Three callers wanted this and each had written its own copy of the same six lines: the
///         encyclopedia card, the XML diagnostics' mega-texture lookup, and the model preview's
///         ability icons. The copies had already drifted - one asked DI for the CONCRETE
///         <c>ModProjectReloadService</c> while only the interface is registered, so its
///         project-configured icon path was a permanent null and it silently used the conventional
///         one.
///     </para>
///     <para>
///         Every failure answers null rather than throwing. Icons are a convenience everywhere they
///         are used, and no arrangement of missing art should take a request down with it.
///     </para>
/// </remarks>
public sealed class WorkspaceIconCatalog(
    ILspConfigurationProvider config,
    IIconCatalogProvider? icons = null,
    IModProjectReloadService? projects = null) : IWorkspaceIconCatalog
{
    /// <inheritdoc />
    public async Task<IconCatalog?> GetAsync(CancellationToken ct)
    {
        if (icons is null)
            return null;

        var layers = IconLayersOf(projects?.LastWorkspaceConfig);
        if (layers.Count == 0)
        {
            // "No layers" means two different things, and answering the wrong one is how the first
            // card opened after startup came back drawn entirely from the baked base game - the
            // wrong portrait, and a 262-wide band on a card the mod had widened to 340.
            //
            // A load that has FINISHED and found no project file leaves nothing but the workspace
            // root to scan, and the heuristic fallback below is the right answer. A load still in
            // flight looks identical from here, and falling back then answers from the base game
            // while the project holding the real art is still being read. Nothing is served until
            // it is known which case this is; the next request gets the real catalog.
            if (projects is { HasLoadedProjects: false })
                return null;

            var root = projects?.LastWorkspaceRoots?.FirstOrDefault() ?? config.Current.WorkspaceRoot;
            if (string.IsNullOrEmpty(root))
                return null;
            layers = [new IconLayer(root, IconProjectSettings.Default)];
        }

        try
        {
            return await icons.GetAsync(layers, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Every layer's icon sources, leaf first, each rooted at its OWN project directory.
    /// </summary>
    /// <remarks>
    ///     A layer with no <c>icons</c> node still takes part, on
    ///     <see cref="IconProjectSettings.Default" />: the engine always looks for the same mega
    ///     texture in the same place, so a dependency that declares nothing can still ship one.
    ///     Layers without a <c>.pgproj</c> have no directory to resolve against and are skipped.
    /// </remarks>
    private static List<IconLayer> IconLayersOf(WorkspaceConfiguration? workspace)
    {
        if (workspace is null)
            return [];

        return workspace.Layers
            .OrderByDescending(layer => layer.Rank)
            .Select(layer => (Root: layer.ProjectDirectory, layer.Icons))
            .Where(l => l.Root is not null)
            .Select(l => new IconLayer(l.Root!, l.Icons ?? IconProjectSettings.Default))
            .ToList();
    }
}