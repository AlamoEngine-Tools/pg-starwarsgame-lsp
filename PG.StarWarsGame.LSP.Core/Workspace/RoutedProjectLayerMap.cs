// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     The <see cref="IProjectLayerMap" /> injected outside a project scope. Ranks are per-project -
///     each root project numbers its own dependency closure from 0 - so a rank only means something
///     together with the project it came from. Routing by URI keeps that pairing implicit for the
///     call sites that only ever ask about one file.
/// </summary>
public sealed class RoutedProjectLayerMap : IProjectLayerMap
{
    private readonly IProjectRegistry _registry;

    public RoutedProjectLayerMap(IProjectRegistry registry)
    {
        _registry = registry;
    }

    public void SetLayers(IReadOnlyList<ProjectLayer> layers)
    {
        _registry.Primary.LayerMap.SetLayers(layers);
    }

    public int GetRank(string fileUri)
    {
        return _registry.ResolvePrimary(fileUri).LayerMap.GetRank(fileUri);
    }

    /// <summary>
    ///     Resolved against the primary project - a bare rank carries no project, so this cannot be
    ///     routed. Callers that have a file in hand should read the name from that file's project.
    /// </summary>
    public string? GetLayerName(int rank)
    {
        return _registry.Primary.LayerMap.GetLayerName(rank);
    }
}
