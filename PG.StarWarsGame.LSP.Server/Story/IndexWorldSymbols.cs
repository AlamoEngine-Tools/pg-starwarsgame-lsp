// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     The simulator's one lookup into the game: does a name the author typed exist, and is it the
///     sort of thing this slot wanted.
/// </summary>
/// <remarks>
///     A planet is decided by the engine's own test - does the object carry the PLANET behaviour -
///     rather than by the element it was declared with or the type name its file gave it. A faction
///     is still a type, because factions are typed by their metafile and cannot be several things.
///     A unit type is any object the index knows.
/// </remarks>
public sealed class IndexWorldSymbols(IGameIndexService indexService, ISchemaProvider schema)
    : IStoryWorldSymbols
{
    public bool Exists(string kind, string name)
    {
        var index = indexService.Current;

        if (kind == StoryWorldSymbolKind.Planet && schema.GetKind(ObjectKindNames.Planet) is { } planet)
        {
            var symbol = index.Resolve(name);
            // Unknown counts as existing: a predicate the index cannot judge must not turn into
            // "no such planet" in a simulation the author is trying to run.
            return symbol is not null && ObjectKinds.Match(index, symbol, planet) != KindMatch.No;
        }

        var typed = kind switch
        {
            StoryWorldSymbolKind.Faction => index.Resolve(name, "Faction"),
            _ => null
        };
        return typed is not null || index.Resolve(name) is not null;
    }
}