// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     The simulator's one lookup into the game: does a name the author typed exist as an emitted
///     symbol. Planets and factions resolve under their own type first; a unit type is any object
///     the index knows, since GameObjectType is an umbrella over many element names.
/// </summary>
public sealed class IndexWorldSymbols(IGameIndexService indexService) : IStoryWorldSymbols
{
    public bool Exists(string kind, string name)
    {
        var index = indexService.Current;
        var typed = kind switch
        {
            StoryWorldSymbolKind.Planet => index.Resolve(name, "Planet"),
            StoryWorldSymbolKind.Faction => index.Resolve(name, "Faction"),
            _ => null
        };
        return typed is not null || index.Resolve(name) is not null;
    }
}
