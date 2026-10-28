// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Variants;

namespace PG.StarWarsGame.LSP.Server.Tests.Variants;

public sealed class GetEffectiveObjectHandlerTest
{
    private static GameSymbol Sym(string id, string? variantBaseId = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit",
            new FileOrigin($"file:///{id}.xml", 0, 0), null, variantBaseId);
    }

    private static GameIndex IndexWith(params GameSymbol[] symbols)
    {
        var defs = symbols.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
            StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with { WorkspaceDefinitions = defs };
    }

    private static GetEffectiveObjectHandler Handler(
        GameIndex index, FakeVariantTagSource source, ILspConfigurationProvider? config = null)
    {
        return new GetEffectiveObjectHandler(new FakeGameIndexService(index), new SchemaProviderBuilder().Build(),
            source,
            TestCatalogues.English);
    }

    [Fact]
    public async Task Handle_Variant_RendersMergedEffectiveXml()
    {
        var index = IndexWith(Sym("V", "B"), Sym("B"));
        var source = new FakeVariantTagSource()
            .With("B", new VariantTag("Max_Health", "100", "<Max_Health>100</Max_Health>", 0))
            .With("V", new VariantTag("Mass", "5", "<Mass>5</Mass>", 0));

        var result = await Handler(index, source)
            .Handle(new GetEffectiveObjectParams { ObjectId = "V" }, CancellationToken.None);

        Assert.True(result.Found);
        Assert.Equal(["V", "B"], result.Chain);
        Assert.Equal("SpaceUnit", result.TypeName);
        Assert.Contains("<Max_Health>100</Max_Health>", result.Xml);
        Assert.Contains("<Mass>5</Mass>", result.Xml);
        Assert.Contains("inherited from B", result.Xml);
    }

    /// <summary>One name may be objects of several types; the view shows the type it was opened on.</summary>
    [Fact]
    public async Task Handle_WithAType_ShowsThatTypesObject()
    {
        var unit = Sym("Sarlacc");
        var faction = new GameSymbol("Sarlacc", GameSymbolKind.XmlObject, "Faction",
            new FileOrigin("file:///factions.xml", 0, 0), null);
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary.Create<string, ImmutableArray<GameSymbol>>(
                StringComparer.OrdinalIgnoreCase).Add("Sarlacc", [unit, faction])
        };
        var source = new FakeVariantTagSource()
            .With(unit, new VariantTag("Tactical_Health", "5", "<Tactical_Health>5</Tactical_Health>", 0))
            .With(faction, new VariantTag("Color", "1, 2, 3", "<Color>1, 2, 3</Color>", 0));

        var result = await Handler(index, source).Handle(
            new GetEffectiveObjectParams { ObjectId = "Sarlacc", TypeName = "Faction" }, CancellationToken.None);

        Assert.Equal("Faction", result.TypeName);
        Assert.Contains("<Color>1, 2, 3</Color>", result.Xml);
        Assert.DoesNotContain("Tactical_Health", result.Xml);
    }

    [Fact]
    public async Task Handle_UnknownId_NotFound()
    {
        var result = await Handler(GameIndex.Empty, new FakeVariantTagSource())
            .Handle(new GetEffectiveObjectParams { ObjectId = "NOPE" }, CancellationToken.None);

        Assert.False(result.Found);
        Assert.Equal(string.Empty, result.Xml);
    }

    [Fact]
    public async Task Handle_CyclicChain_FlagsCyclic()
    {
        var index = IndexWith(Sym("A", "B"), Sym("B", "A"));
        var source = new FakeVariantTagSource().With("A", new VariantTag("X", "1", "<X>1</X>", 0));

        var result = await Handler(index, source)
            .Handle(new GetEffectiveObjectParams { ObjectId = "A" }, CancellationToken.None);

        Assert.True(result.Cyclic);
    }
}