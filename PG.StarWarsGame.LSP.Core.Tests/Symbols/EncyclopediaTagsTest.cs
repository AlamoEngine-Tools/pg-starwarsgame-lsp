// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

/// <summary>
///     Which objects have an encyclopedia card: the ones whose text defines a body, a planet's aside -
///     a planet has a preview of its own, and the unit card is not it (maintainer, 2026-10-22).
/// </summary>
public sealed class EncyclopediaTagsTest
{
    private static readonly ObjectKindDefinition s_planet = new()
    {
        Kind = ObjectKindNames.Planet, Behaviors = ["PLANET"]
    };

    private sealed class Tags : IVariantTagSource
    {
        public IReadOnlyList<VariantTag> TryGetTags(string objectId)
        {
            return objectId is "Star_Destroyer" or "Coruscant"
                ? [new VariantTag(EncyclopediaTags.Body, "TEXT_X", "<Encyclopedia_Text>TEXT_X</Encyclopedia_Text>", 0)]
                : [];
        }
    }

    private static (GameIndex Index, GameSymbol Symbol) Object(string id, string[] behaviors)
    {
        var symbol = new GameSymbol(id, GameSymbolKind.XmlObject, "GameObjectType", new UnknownOrigin("test"), null,
            null, behaviors);
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add(id, [symbol])
        };
        return (index, symbol);
    }

    [Fact]
    public void AnObjectWithBodyText_HasACard()
    {
        var (index, symbol) = Object("Star_Destroyer", ["DUMMY_STARSHIP"]);

        Assert.True(EncyclopediaTags.HasCard(index, new Tags(), symbol, s_planet));
    }

    [Fact]
    public void APlanet_HasNoCard_ThoughItsTextDefinesABody()
    {
        var (index, symbol) = Object("Coruscant", ["PLANET"]);

        Assert.True(EncyclopediaTags.DefinesBody(index, new Tags(), symbol));
        Assert.False(EncyclopediaTags.HasCard(index, new Tags(), symbol, s_planet));
    }

    [Fact]
    public void AnObjectWithoutBodyText_HasNoCard()
    {
        var (index, symbol) = Object("Tie_Fighter", ["DUMMY_STARSHIP"]);

        Assert.False(EncyclopediaTags.HasCard(index, new Tags(), symbol, s_planet));
    }

    // A schema without the kind cannot tell a planet; the body text alone decides, as before.
    [Fact]
    public void WithoutThePlanetKind_TheBodyTextAloneDecides()
    {
        var (index, symbol) = Object("Coruscant", ["PLANET"]);

        Assert.True(EncyclopediaTags.HasCard(index, new Tags(), symbol, null));
    }
}
