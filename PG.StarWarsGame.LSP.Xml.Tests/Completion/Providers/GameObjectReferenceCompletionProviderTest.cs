// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion.Providers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion.Providers;

public sealed class GameObjectReferenceCompletionProviderTest
{
    private static readonly GameObjectReferenceCompletionProvider Provider = new();

    private static XmlTagDefinition RefTag(string typeName)
    {
        return new XmlTagDefinition
        {
            Tag = "Target",
            ValueType = XmlValueType.NameReference,
            ObjectType = new GameObjectTypeDefinition { TypeName = typeName }
        };
    }

    private static GameIndex IndexWith(params (string id, string typeName)[] symbols)
    {
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty;
        foreach (var (id, typeName) in symbols)
        {
            var sym = new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new UnknownOrigin(""), null);
            defs = defs.Add(id, [sym]);
        }

        return GameIndex.Empty with { WorkspaceDefinitions = defs };
    }

    // ── CanHandle ────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_NameReference_True()
    {
        var tag = new XmlTagDefinition { Tag = "T", ValueType = XmlValueType.NameReference, ReferenceKind = ReferenceKind.XmlObject };
        Assert.True(Provider.CanHandle(tag));
    }

    [Fact]
    public void CanHandle_NameReferenceList_True()
    {
        var tag = new XmlTagDefinition { Tag = "T", ValueType = XmlValueType.NameReferenceList, ReferenceKind = ReferenceKind.XmlObject };
        Assert.True(Provider.CanHandle(tag));
    }

    [Fact]
    public void CanHandle_SFXEventReference_True()
    {
        var tag = new XmlTagDefinition { Tag = "T", ValueType = XmlValueType.SFXEventReference, ReferenceKind = ReferenceKind.XmlObject };
        Assert.True(Provider.CanHandle(tag));
    }

    [Fact]
    public void CanHandle_FactionReference_True()
    {
        var tag = new XmlTagDefinition { Tag = "T", ValueType = XmlValueType.FactionReference, ReferenceKind = ReferenceKind.XmlObject };
        Assert.True(Provider.CanHandle(tag));
    }

    [Fact]
    public void CanHandle_TypeReference_True()
    {
        var tag = new XmlTagDefinition { Tag = "T", ValueType = XmlValueType.TypeReference, ReferenceKind = ReferenceKind.XmlObject };
        Assert.True(Provider.CanHandle(tag));
    }

    [Fact]
    public void CanHandle_TypeReferenceList_True()
    {
        var tag = new XmlTagDefinition { Tag = "T", ValueType = XmlValueType.TypeReferenceList, ReferenceKind = ReferenceKind.XmlObject };
        Assert.True(Provider.CanHandle(tag));
    }

    [Fact]
    public void CanHandle_ReferenceKind_None_False()
    {
        var tag = new XmlTagDefinition { Tag = "T", ValueType = XmlValueType.NameReference, ReferenceKind = ReferenceKind.None };
        Assert.False(Provider.CanHandle(tag));
    }

    [Fact]
    public void CanHandle_Float_False()
    {
        var tag = new XmlTagDefinition { Tag = "T", ValueType = XmlValueType.Float };
        Assert.False(Provider.CanHandle(tag));
    }

    [Fact]
    public void CanHandle_Boolean_False()
    {
        var tag = new XmlTagDefinition { Tag = "T", ValueType = XmlValueType.Boolean };
        Assert.False(Provider.CanHandle(tag));
    }

    // ── GetProposals — no ObjectType ─────────────────────────────────────────

    [Fact]
    public void GetProposals_TagHasNoObjectType_ReturnsEmpty()
    {
        var tag = new XmlTagDefinition { Tag = "T", ValueType = XmlValueType.NameReference };
        var index = IndexWith(("X_Wing", "SpaceUnit"));

        var result = Provider.GetProposals(tag, "", index);

        Assert.Empty(result);
    }

    // ── GetProposals — type filtering ────────────────────────────────────────

    [Fact]
    public void GetProposals_FiltersSymbolsByObjectType()
    {
        var tag = RefTag("SpaceUnit");
        var index = IndexWith(("X_Wing", "SpaceUnit"), ("REBEL_ALLIANCE", "Faction"));

        var result = Provider.GetProposals(tag, "", index);

        Assert.Single(result);
        Assert.Equal("X_Wing", result[0].Label);
    }

    [Fact]
    public void GetProposals_TypeMatchIsCaseInsensitive()
    {
        var tag = RefTag("spaceunit");
        var index = IndexWith(("X_Wing", "SpaceUnit"));

        var result = Provider.GetProposals(tag, "", index);

        Assert.Single(result);
    }

    // ── GetProposals — prefix filtering ──────────────────────────────────────

    [Fact]
    public void GetProposals_FiltersSymbolsByPartialValue()
    {
        var tag = RefTag("SpaceUnit");
        var index = IndexWith(("X_Wing", "SpaceUnit"), ("Y_Wing", "SpaceUnit"));

        var result = Provider.GetProposals(tag, "X", index);

        Assert.Single(result);
        Assert.Equal("X_Wing", result[0].Label);
    }

    [Fact]
    public void GetProposals_EmptyPartial_ReturnsAllMatchingSymbols()
    {
        var tag = RefTag("SpaceUnit");
        var index = IndexWith(("X_Wing", "SpaceUnit"), ("Y_Wing", "SpaceUnit"));

        var result = Provider.GetProposals(tag, "", index);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetProposals_PrefixMatchIsCaseInsensitive()
    {
        var tag = RefTag("SpaceUnit");
        var index = IndexWith(("X_Wing", "SpaceUnit"));

        var result = Provider.GetProposals(tag, "x_", index);

        Assert.Single(result);
    }

    // ── GetProposals — baseline symbols ──────────────────────────────────────

    [Fact]
    public void GetProposals_IncludesBaselineSymbols()
    {
        var tag = RefTag("SpaceUnit");
        var baselineSym = new GameSymbol("Millennium_Falcon", GameSymbolKind.XmlObject, "SpaceUnit",
            new UnknownOrigin(""), null);
        var index = GameIndex.Empty with
        {
            Baseline = new BaselineIndex(
                ImmutableDictionary<string, GameSymbol>.Empty.Add("Millennium_Falcon", baselineSym),
                DateTimeOffset.UtcNow, "hash",
                ImmutableDictionary<string, ImmutableArray<string>>.Empty,
                ImmutableDictionary<string, ImmutableArray<string>>.Empty,
                ImmutableDictionary<string, ImmutableArray<string>>.Empty)
        };

        var result = Provider.GetProposals(tag, "", index);

        Assert.Single(result);
        Assert.Equal("Millennium_Falcon", result[0].Label);
    }

    // ── GetProposals — Detail from Description ───────────────────────────────

    [Fact]
    public void GetProposals_SymbolWithDescription_SetsDetail()
    {
        var tag = RefTag("SpaceUnit");
        var sym = new GameSymbol("X_Wing", GameSymbolKind.XmlObject, "SpaceUnit",
            new UnknownOrigin(""), "Rebel starfighter");
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("X_Wing", [sym])
        };

        var result = Provider.GetProposals(tag, "", index);

        Assert.Single(result);
        Assert.Equal("Rebel starfighter", result[0].Detail);
    }

    // ── GetProposals — GameObjectType wildcard ───────────────────────────────

    [Fact]
    public void GetProposals_GameObjectType_ReturnsAllSymbolsRegardlessOfType()
    {
        var tag = RefTag("GameObjectType");
        var index = IndexWith(("X_Wing", "SpaceUnit"), ("AT_AT", "GroundCompanyUnit"), ("REBEL_ALLIANCE", "Faction"));

        var result = Provider.GetProposals(tag, "", index);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void GetProposals_GameObjectType_StillFiltersOnPartialValue()
    {
        var tag = RefTag("GameObjectType");
        var index = IndexWith(("X_Wing", "SpaceUnit"), ("AT_AT", "GroundCompanyUnit"));

        var result = Provider.GetProposals(tag, "X", index);

        Assert.Single(result);
        Assert.Equal("X_Wing", result[0].Label);
    }

    [Fact]
    public void GetProposals_GameObjectType_CaseInsensitive()
    {
        var tag = RefTag("gameobjecttype");
        var index = IndexWith(("X_Wing", "SpaceUnit"), ("AT_AT", "GroundCompanyUnit"));

        var result = Provider.GetProposals(tag, "", index);

        Assert.Equal(2, result.Count);
    }
}