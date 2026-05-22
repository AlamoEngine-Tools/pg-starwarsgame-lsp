// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion;

file sealed class StubProposalSchema : ISchemaProvider
{
    private readonly Dictionary<string, EnumDefinition> _enums;

    public StubProposalSchema(params EnumDefinition[] enums)
    {
        _enums = enums.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
    }

    public EnumDefinition? GetEnum(string name)
    {
        return _enums.GetValueOrDefault(name);
    }

    public IReadOnlyList<EnumDefinition> AllEnums => [.. _enums.Values];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];

    public XmlTagDefinition? GetTag(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return [];
    }

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

public sealed class StoryParamValueProposalProviderTest
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static EnumDefinition MakeEnum(string name, params string[] values)
    {
        return new EnumDefinition
        {
            Name = name,
            Kind = EnumKind.SchemaFixed,
            Values = [.. values.Select(v => new EnumValueDefinition { Name = v })]
        };
    }

    private static ParamDefinition EnumParam(string enumName)
    {
        return new ParamDefinition
        {
            Position = 0,
            ValueType = XmlValueType.DynamicEnumValue,
            EnumName = enumName
        };
    }

    private static ParamDefinition BoolParam()
    {
        return new ParamDefinition
        {
            Position = 0,
            ValueType = XmlValueType.Boolean
        };
    }

    private static ParamDefinition RefParam(string referenceType,
        XmlValueType valueType = XmlValueType.NameReference)
    {
        return new ParamDefinition
        {
            Position = 0,
            ValueType = valueType,
            ReferenceType = referenceType
        };
    }

    private static ParamDefinition ScalarParam(XmlValueType valueType)
    {
        return new ParamDefinition
        {
            Position = 0,
            ValueType = valueType
        };
    }

    private static GameIndex IndexWithSymbols(params (string id, string typeName)[] symbols)
    {
        var dict = ImmutableDictionary.CreateRange(
            StringComparer.OrdinalIgnoreCase,
            symbols.Select(s => KeyValuePair.Create(s.id,
                new GameSymbol(s.id, GameSymbolKind.XmlObject, s.typeName, new UnknownOrigin("test"), null))));
        return GameIndex.Empty with { Baseline = BaselineIndex.Empty with { Symbols = dict } };
    }

    private static GameIndex IndexWithWorkspaceSymbols(params (string id, string typeName)[] symbols)
    {
        var ws = symbols.ToImmutableDictionary(
            s => s.id,
            s => ImmutableArray.Create(
                new GameSymbol(s.id, GameSymbolKind.XmlObject, s.typeName, new UnknownOrigin("test"), null)),
            StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with { WorkspaceDefinitions = ws };
    }

    // ── DynamicEnumValue proposals ───────────────────────────────────────────

    [Fact]
    public void GetProposals_DynamicEnumValue_ReturnsAllValuesWhenPartialEmpty()
    {
        var sut = new StoryParamValueProposalProvider(
            new StubProposalSchema(MakeEnum("FlagCmp", "GREATER_THAN", "LESS_THAN", "EQUAL_TO")));

        var proposals = sut.GetProposals(EnumParam("FlagCmp"), "", GameIndex.Empty);

        Assert.Equal(3, proposals.Count);
    }

    [Fact]
    public void GetProposals_DynamicEnumValue_FiltersByPartialPrefix()
    {
        var sut = new StoryParamValueProposalProvider(
            new StubProposalSchema(MakeEnum("FlagCmp", "GREATER_THAN", "LESS_THAN", "EQUAL_TO")));

        var proposals = sut.GetProposals(EnumParam("FlagCmp"), "G", GameIndex.Empty);

        Assert.Single(proposals);
        Assert.Equal("GREATER_THAN", proposals[0].Label);
    }

    [Fact]
    public void GetProposals_DynamicEnumValue_ReturnsEmptyWhenEnumNotInSchema()
    {
        var sut = new StoryParamValueProposalProvider(new StubProposalSchema());

        Assert.Empty(sut.GetProposals(EnumParam("UnknownEnum"), "", GameIndex.Empty));
    }

    // ── Boolean proposals ────────────────────────────────────────────────────

    [Fact]
    public void GetProposals_Boolean_Returns0And1()
    {
        var sut = new StoryParamValueProposalProvider(new StubProposalSchema());

        var proposals = sut.GetProposals(BoolParam(), "", GameIndex.Empty);

        var labels = proposals.Select(p => p.Label).ToList();
        Assert.Contains("0", labels);
        Assert.Contains("1", labels);
    }

    [Fact]
    public void GetProposals_Boolean_FiltersByPartialPrefix()
    {
        var sut = new StoryParamValueProposalProvider(new StubProposalSchema());

        var proposals = sut.GetProposals(BoolParam(), "1", GameIndex.Empty);

        Assert.Single(proposals);
        Assert.Equal("1", proposals[0].Label);
    }

    // ── NameReference proposals ──────────────────────────────────────────────

    [Fact]
    public void GetProposals_NameReference_ReturnsMatchingBaselineSymbols()
    {
        var sut = new StoryParamValueProposalProvider(new StubProposalSchema());
        var index = IndexWithSymbols(("Coruscant", "Planet"), ("Tatooine", "Planet"), ("Yavin_4", "StarBase"));

        var proposals = sut.GetProposals(RefParam("Planet"), "", index);

        var labels = proposals.Select(p => p.Label).ToList();
        Assert.Contains("Coruscant", labels);
        Assert.Contains("Tatooine", labels);
        Assert.DoesNotContain("Yavin_4", labels);
    }

    [Fact]
    public void GetProposals_NameReference_FiltersByPartialPrefix()
    {
        var sut = new StoryParamValueProposalProvider(new StubProposalSchema());
        var index = IndexWithSymbols(("Coruscant", "Planet"), ("Tatooine", "Planet"));

        var proposals = sut.GetProposals(RefParam("Planet"), "C", index);

        Assert.Single(proposals);
        Assert.Equal("Coruscant", proposals[0].Label);
    }

    // ── NameReferenceList proposals ──────────────────────────────────────────

    [Fact]
    public void GetProposals_NameReferenceList_ReturnsMatchingIndexSymbols()
    {
        var sut = new StoryParamValueProposalProvider(new StubProposalSchema());
        var index = IndexWithWorkspaceSymbols(("X_Wing", "GameObjectType"), ("TIE_Fighter", "GameObjectType"));

        var proposals = sut.GetProposals(
            RefParam("GameObjectType", XmlValueType.NameReferenceList), "", index);

        var labels = proposals.Select(p => p.Label).ToList();
        Assert.Contains("X_Wing", labels);
        Assert.Contains("TIE_Fighter", labels);
    }

    // ── Scalar kinds return empty ─────────────────────────────────────────────

    [Theory]
    [InlineData(XmlValueType.Int)]
    [InlineData(XmlValueType.Float)]
    [InlineData(XmlValueType.FloatVector3)]
    [InlineData(XmlValueType.NameReference)] // no ReferenceType set → no proposals
    public void GetProposals_ScalarOrUnconstrainedRef_ReturnsEmpty(XmlValueType valueType)
    {
        var sut = new StoryParamValueProposalProvider(new StubProposalSchema());
        // For NameReference with null ReferenceType: no filter → empty
        var param = new ParamDefinition { Position = 0, ValueType = valueType };

        Assert.Empty(sut.GetProposals(param, "", GameIndex.Empty));
    }

    // ── Null param → empty ───────────────────────────────────────────────────

    [Fact]
    public void GetProposals_NullParam_ReturnsEmpty()
    {
        var sut = new StoryParamValueProposalProvider(new StubProposalSchema());

        Assert.Empty(sut.GetProposals(null, "", GameIndex.Empty));
    }
}