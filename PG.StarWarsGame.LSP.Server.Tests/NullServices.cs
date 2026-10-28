// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Assets.ShipNames;
using PG.StarWarsGame.LSP.Server.Icons;
using PG.StarWarsGame.LSP.Server.ShipNames;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Server.Tests;

// The server's dependencies are all required (#305); a test that is not about one of them hands
// over the quietest implementation there is. Each answers what the real service answers when it
// has nothing: no icon catalog, no ship-name pools, a world where every name exists.

/// <summary>A workspace with no icon catalog: the card and the preview draw no art.</summary>
internal sealed class NoWorkspaceIcons : IWorkspaceIconCatalog
{
    public Task<IconCatalog?> GetAsync(CancellationToken ct)
    {
        return Task.FromResult<IconCatalog?>(null);
    }
}

/// <summary>
///     A world in which every planet, unit and faction an author names exists: the simulator checks
///     nothing, as it did before the symbols were wired, so a test about its clock or its flags is
///     not also a test about the index.
/// </summary>
internal sealed class AnyWorldSymbols : IStoryWorldSymbols
{
    public bool Exists(string kind, string name)
    {
        return true;
    }
}

/// <summary>A project whose ship-name files hold no pools.</summary>
internal sealed class NoShipNames : IShipNameCatalogProvider
{
    public ShipNameCatalog Get(string projectRoot, string? rawTagValue)
    {
        return ShipNameCatalog.Empty;
    }
}