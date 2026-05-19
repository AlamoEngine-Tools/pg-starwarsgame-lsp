// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion;
using PG.StarWarsGame.LSP.Xml.StoryScripting;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion;

file sealed class StubSchemaForProposals : ISchemaProvider
{
    private readonly Dictionary<string, EnumDefinition> _enums;

    public StubSchemaForProposals(params EnumDefinition[] enums)
    {
        _enums = enums.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
    }

    public EnumDefinition? GetEnum(string name) => _enums.GetValueOrDefault(name);
    public IReadOnlyList<EnumDefinition> AllEnums => [.. _enums.Values];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public XmlTagDefinition? GetTag(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
    public GameObjectTypeDefinition? GetObjectType(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];
    public event EventHandler? SchemaRefreshed { add { } remove { } }
}

public sealed class StoryParamValueProposalProviderTest
{
    private static EnumDefinition MakeEnum(string name, params string[] values) => new()
    {
        Name = name,
        Kind = EnumKind.SchemaFixed,
        Values = [.. values.Select(v => new EnumValueDefinition { Name = v })]
    };

    private static StoryParamDefinition EnumParam(string enumName) =>
        new(1, StoryParamKind.Enum, Required: true, EnumName: enumName);

    private static StoryParamDefinition RefParam(StoryParamKind kind, string refType) =>
        new(1, kind, Required: true, ReferenceType: refType);

    private static StoryParamDefinition SimpleParam(StoryParamKind kind) =>
        new(1, kind, Required: false);

    private static GameIndex IndexWithSymbols(params (string id, string typeName)[] symbols)
    {
        var baseline = BaselineIndex.Empty with
        {
            Symbols = symbols.ToImmutableDictionary(
                s => s.id,
                s => new GameSymbol(s.id, GameSymbolKind.XmlObject, s.typeName, new UnknownOrigin("test"), null),
                StringComparer.OrdinalIgnoreCase)
        };
        return GameIndex.Empty with { Baseline = baseline };
    }

    private static GameIndex IndexWithWorkspaceSymbols(params (string id, string typeName)[] symbols)
    {
        var ws = symbols.ToImmutableDictionary(
            s => s.id,
            s => ImmutableArray.Create(new GameSymbol(s.id, GameSymbolKind.XmlObject, s.typeName, new UnknownOrigin("test"), null)),
            StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with { WorkspaceDefinitions = ws };
    }

    // ── Enum proposals ────────────────────────────────────────────────────────

    [Fact]
    public void Enum_returns_all_values_when_partial_is_empty()
    {
        var sut = new StoryParamValueProposalProvider(
            new StubSchemaForProposals(MakeEnum("FlagCmp", "GREATER_THAN", "LESS_THAN", "EQUAL_TO")));

        var proposals = sut.GetProposals(EnumParam("FlagCmp"), "", GameIndex.Empty);

        Assert.Equal(3, proposals.Count);
    }

    [Fact]
    public void Enum_filters_by_partial_prefix()
    {
        var sut = new StoryParamValueProposalProvider(
            new StubSchemaForProposals(MakeEnum("FlagCmp", "GREATER_THAN", "LESS_THAN", "EQUAL_TO")));

        var proposals = sut.GetProposals(EnumParam("FlagCmp"), "G", GameIndex.Empty);

        Assert.Single(proposals);
        Assert.Equal("GREATER_THAN", proposals[0].Label);
    }

    [Fact]
    public void Enum_returns_empty_when_enum_not_in_schema()
    {
        var sut = new StoryParamValueProposalProvider(new StubSchemaForProposals());

        var proposals = sut.GetProposals(EnumParam("UnknownEnum"), "", GameIndex.Empty);

        Assert.Empty(proposals);
    }

    [Fact]
    public void EnumList_returns_same_proposals_as_Enum()
    {
        var sut = new StoryParamValueProposalProvider(
            new StubSchemaForProposals(MakeEnum("Triggers", "END_SETUP", "CLICK")));
        var def = new StoryParamDefinition(1, StoryParamKind.EnumList, Required: false, EnumName: "Triggers");

        var proposals = sut.GetProposals(def, "", GameIndex.Empty);

        Assert.Equal(2, proposals.Count);
    }

    // ── BooleanInt proposals ──────────────────────────────────────────────────

    [Fact]
    public void BooleanInt_returns_0_and_1()
    {
        var sut = new StoryParamValueProposalProvider(new StubSchemaForProposals());

        var proposals = sut.GetProposals(SimpleParam(StoryParamKind.BooleanInt), "", GameIndex.Empty);

        var labels = proposals.Select(p => p.Label).ToList();
        Assert.Contains("0", labels);
        Assert.Contains("1", labels);
    }

    [Fact]
    public void BooleanInt_partial_filters_results()
    {
        var sut = new StoryParamValueProposalProvider(new StubSchemaForProposals());

        var proposals = sut.GetProposals(SimpleParam(StoryParamKind.BooleanInt), "1", GameIndex.Empty);

        Assert.Single(proposals);
        Assert.Equal("1", proposals[0].Label);
    }

    // ── Ref proposals — baseline ──────────────────────────────────────────────

    [Fact]
    public void PlanetRef_returns_matching_baseline_symbols()
    {
        var sut = new StoryParamValueProposalProvider(new StubSchemaForProposals());
        var index = IndexWithSymbols(("Coruscant", "Planet"), ("Tatooine", "Planet"), ("Yavin_4", "StarBase"));

        var proposals = sut.GetProposals(RefParam(StoryParamKind.PlanetRef, "Planet"), "", index);

        var labels = proposals.Select(p => p.Label).ToList();
        Assert.Contains("Coruscant", labels);
        Assert.Contains("Tatooine", labels);
        Assert.DoesNotContain("Yavin_4", labels);
    }

    [Fact]
    public void Ref_partial_filters_by_id_prefix()
    {
        var sut = new StoryParamValueProposalProvider(new StubSchemaForProposals());
        var index = IndexWithSymbols(("Coruscant", "Planet"), ("Tatooine", "Planet"));

        var proposals = sut.GetProposals(RefParam(StoryParamKind.PlanetRef, "Planet"), "C", index);

        Assert.Single(proposals);
        Assert.Equal("Coruscant", proposals[0].Label);
    }

    // ── Ref proposals — workspace ─────────────────────────────────────────────

    [Fact]
    public void GameObjectTypeRef_returns_workspace_definition_symbols()
    {
        var sut = new StoryParamValueProposalProvider(new StubSchemaForProposals());
        var index = IndexWithWorkspaceSymbols(("X_Wing", "GameObjectType"), ("TIE_Fighter", "GameObjectType"));

        var proposals = sut.GetProposals(RefParam(StoryParamKind.GameObjectTypeRef, "GameObjectType"), "", index);

        var labels = proposals.Select(p => p.Label).ToList();
        Assert.Contains("X_Wing", labels);
        Assert.Contains("TIE_Fighter", labels);
    }

    // ── Scalar kinds return empty ─────────────────────────────────────────────

    [Theory]
    [InlineData(StoryParamKind.Integer)]
    [InlineData(StoryParamKind.PositiveInteger)]
    [InlineData(StoryParamKind.FloatSeconds)]
    [InlineData(StoryParamKind.FreeString)]
    [InlineData(StoryParamKind.EraNumber)]
    [InlineData(StoryParamKind.TechLevel)]
    public void Scalar_kind_returns_empty(StoryParamKind kind)
    {
        var sut = new StoryParamValueProposalProvider(new StubSchemaForProposals());

        var proposals = sut.GetProposals(SimpleParam(kind), "", GameIndex.Empty);

        Assert.Empty(proposals);
    }
}
