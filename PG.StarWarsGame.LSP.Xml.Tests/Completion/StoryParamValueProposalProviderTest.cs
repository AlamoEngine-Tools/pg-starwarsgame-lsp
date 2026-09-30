// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion;

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

    private static ParamDefinition EnumParam(EnumDefinition? enumDef = null)
    {
        return new ParamDefinition
        {
            Position = 0,
            ValueType = XmlValueType.DynamicEnumValue,
            Enum = enumDef
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
            ObjectType = new GameObjectTypeDefinition { TypeName = referenceType }
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

    /// <summary>A story-style param: raw referenceType string only, no resolved ObjectType.</summary>
    private static ParamDefinition RawRefParam(string referenceTypeName,
        XmlValueType valueType = XmlValueType.NameReference)
    {
        return new ParamDefinition
        {
            Position = 0,
            ValueType = valueType,
            ReferenceTypeName = referenceTypeName
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

    private static readonly ObjectKindDefinition PlanetKind = new()
    {
        Kind = "Planet", Behaviors = ["PLANET"]
    };

    private static ParamDefinition KindParam(ObjectKindDefinition kind,
        XmlValueType valueType = XmlValueType.NameReference)
    {
        return new ParamDefinition
        {
            Position = 0,
            ValueType = valueType,
            ReferenceTypeName = kind.Kind,
            Kind = kind
        };
    }

    private static GameIndex IndexWithBehaviours(params (string id, string[] behaviors)[] symbols)
    {
        var ws = symbols.ToImmutableDictionary(
            s => s.id,
            s => ImmutableArray.Create(new GameSymbol(s.id, GameSymbolKind.XmlObject, "GameObjectType",
                new UnknownOrigin("test"), null, null, s.behaviors)),
            StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with { WorkspaceDefinitions = ws };
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
        var sut = new StoryParamValueProposalProvider();

        var proposals = sut.GetProposals(EnumParam(MakeEnum("FlagCmp", "GREATER_THAN", "LESS_THAN", "EQUAL_TO")), "",
            GameIndex.Empty);

        Assert.Equal(3, proposals.Count);
    }

    [Fact]
    public void GetProposals_DynamicEnumValue_FiltersByPartialPrefix()
    {
        var sut = new StoryParamValueProposalProvider();

        var proposals = sut.GetProposals(EnumParam(MakeEnum("FlagCmp", "GREATER_THAN", "LESS_THAN", "EQUAL_TO")), "G",
            GameIndex.Empty);

        Assert.Single(proposals);
        Assert.Equal("GREATER_THAN", proposals[0].Label);
    }

    [Fact]
    public void GetProposals_DynamicEnumValue_ReturnsEmptyWhenEnumNotInSchema()
    {
        var sut = new StoryParamValueProposalProvider();

        Assert.Empty(sut.GetProposals(EnumParam(), "", GameIndex.Empty));
    }

    // ── Boolean proposals ────────────────────────────────────────────────────

    [Fact]
    public void GetProposals_Boolean_Returns0And1()
    {
        var sut = new StoryParamValueProposalProvider();

        var proposals = sut.GetProposals(BoolParam(), "", GameIndex.Empty);

        var labels = proposals.Select(p => p.Label).ToList();
        Assert.Contains("0", labels);
        Assert.Contains("1", labels);
    }

    [Fact]
    public void GetProposals_Boolean_FiltersByPartialPrefix()
    {
        var sut = new StoryParamValueProposalProvider();

        var proposals = sut.GetProposals(BoolParam(), "1", GameIndex.Empty);

        Assert.Single(proposals);
        Assert.Equal("1", proposals[0].Label);
    }

    // ── NameReference proposals ──────────────────────────────────────────────

    [Fact]
    public void GetProposals_NameReference_ReturnsMatchingBaselineSymbols()
    {
        var sut = new StoryParamValueProposalProvider();
        // Faction is a type of its own in the index; Planet is not (see the umbrella tests).
        var index = IndexWithSymbols(("Rebel", "Faction"), ("Empire", "Faction"), ("Yavin_4", "StarBase"));

        var proposals = sut.GetProposals(RefParam("Faction"), "", index);

        var labels = proposals.Select(p => p.Label).ToList();
        Assert.Contains("Rebel", labels);
        Assert.Contains("Empire", labels);
        Assert.DoesNotContain("Yavin_4", labels);
    }

    [Fact]
    public void GetProposals_NameReference_FiltersByPartialPrefix()
    {
        var sut = new StoryParamValueProposalProvider();
        var index = IndexWithSymbols(("Coruscant", "Planet"), ("Tatooine", "Planet"));

        var proposals = sut.GetProposals(RefParam("Planet"), "C", index);

        Assert.Single(proposals);
        Assert.Equal("Coruscant", proposals[0].Label);
    }

    // ── NameReferenceList proposals ──────────────────────────────────────────

    [Fact]
    public void GetProposals_NameReferenceList_ReturnsMatchingIndexSymbols()
    {
        var sut = new StoryParamValueProposalProvider();
        var index = IndexWithWorkspaceSymbols(("X_Wing", "GameObjectType"), ("TIE_Fighter", "GameObjectType"));

        var proposals = sut.GetProposals(
            RefParam("GameObjectType", XmlValueType.NameReferenceList), "", index);

        var labels = proposals.Select(p => p.Label).ToList();
        Assert.Contains("X_Wing", labels);
        Assert.Contains("TIE_Fighter", labels);
    }

    // ── Raw referenceType fallback (story params carry no resolved ObjectType) ─

    [Fact]
    public void GetProposals_NameReference_FallsBackToRawReferenceTypeName()
    {
        var sut = new StoryParamValueProposalProvider();
        var index = IndexWithSymbols(("Rebel", "Faction"), ("X_Wing", "SpaceUnit"));

        var proposals = sut.GetProposals(RawRefParam("Faction"), "", index);

        Assert.Single(proposals);
        Assert.Equal("Rebel", proposals[0].Label);
    }

    [Fact]
    public void GetProposals_GameObjectTypeUmbrella_ProposesEveryConcreteObjectType()
    {
        var sut = new StoryParamValueProposalProvider();
        var index = IndexWithWorkspaceSymbols(("X_Wing", "SpaceUnit"), ("Vader_Team", "HeroCompany"));

        var proposals = sut.GetProposals(
            RawRefParam("GameObjectType", XmlValueType.NameReferenceList), "", index);

        var labels = proposals.Select(p => p.Label).ToList();
        Assert.Contains("X_Wing", labels);
        Assert.Contains("Vader_Team", labels);
    }

    // A planet slot takes whatever the engine would accept as a planet: an object carrying the
    // PLANET behaviour, whatever element it was declared with and whatever type the index gave it.
    [Fact]
    public void GetProposals_PlanetKind_ProposesOnlyObjectsWithThatBehaviour()
    {
        var sut = new StoryParamValueProposalProvider();
        var index = IndexWithBehaviours(
            ("Kashyyyk", ["PLANET", "PRODUCTION"]),
            ("Kessel", ["PLANET"]),
            ("X_Wing", ["DUMMY_STARSHIP"]));

        var labels = sut.GetProposals(KindParam(PlanetKind), "", index).Select(p => p.Label).ToList();

        Assert.Equal(["Kashyyyk", "Kessel"], labels.Order());
    }

    [Fact]
    public void GetProposals_PlanetKind_StillFiltersByPrefix()
    {
        var sut = new StoryParamValueProposalProvider();
        var index = IndexWithBehaviours(("Kashyyyk", ["PLANET"]), ("Kessel", ["PLANET"]));

        var labels = sut.GetProposals(KindParam(PlanetKind), "Kas", index).Select(p => p.Label).ToList();

        Assert.Equal(["Kashyyyk"], labels);
    }

    // A variant of a planet is a planet - it inherits the behaviour list it does not override.
    [Fact]
    public void GetProposals_PlanetKind_ProposesVariantsOfAPlanet()
    {
        var sut = new StoryParamValueProposalProvider();
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = new[]
                {
                    new GameSymbol("Base_Planet", GameSymbolKind.XmlObject, "GameObjectType",
                        new UnknownOrigin("test"), null, null, ["PLANET"]),
                    new GameSymbol("Modded_Planet", GameSymbolKind.XmlObject, "GameObjectType",
                        new UnknownOrigin("test"), null, "Base_Planet")
                }
                .ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
                    StringComparer.OrdinalIgnoreCase)
        };

        var labels = sut.GetProposals(KindParam(PlanetKind), "Mod", index).Select(p => p.Label).ToList();

        Assert.Equal(["Modded_Planet"], labels);
    }

    // Story symbols live in the same index and are never objects, so a kind slot must not offer
    // them even when the predicate cannot be judged against them.
    [Fact]
    public void GetProposals_PlanetKind_ExcludesStorySymbols()
    {
        var sut = new StoryParamValueProposalProvider();
        var index = IndexWithWorkspaceSymbols(("Open_Act_1", "StoryEvent"), ("Some_Flag", "StoryFlag"));

        Assert.Empty(sut.GetProposals(KindParam(PlanetKind), "", index));
    }

    [Fact]
    public void GetProposals_GameObjectTypeUmbrella_ExcludesStorySymbolsAndThreadObjects()
    {
        var sut = new StoryParamValueProposalProvider();
        var index = IndexWithWorkspaceSymbols(
            ("X_Wing", "SpaceUnit"),
            ("Some_Flag", "StoryFlag"),
            ("Some_Event", "StoryEvent"),
            ("Some_Notification", "StoryNotification"),
            ("Thread_Event_Obj", "StoryParser"));

        var proposals = sut.GetProposals(RawRefParam("GameObjectType"), "", index);

        Assert.Single(proposals);
        Assert.Equal("X_Wing", proposals[0].Label);
    }

    [Fact]
    public void GetProposals_ScopedAbilityIds_ProposeAndFilterByStrippedDisplayName()
    {
        var sut = new StoryParamValueProposalProvider();
        var index = IndexWithWorkspaceSymbols(("MY_UNIT$Medic_Healing", "UnitAbility"));

        var proposals = sut.GetProposals(RawRefParam("UnitAbility"), "Med", index);

        Assert.Single(proposals);
        Assert.Equal("Medic_Healing", proposals[0].Label);
    }

    [Fact]
    public void GetProposals_StoryEventNameRefType_ProposesStoryEventSymbols()
    {
        var sut = new StoryParamValueProposalProvider();
        var index = IndexWithWorkspaceSymbols(("Open_Act_1", "StoryEvent"), ("X_Wing", "SpaceUnit"));

        var proposals = sut.GetProposals(RawRefParam("StoryEventName"), "", index);

        Assert.Single(proposals);
        Assert.Equal("Open_Act_1", proposals[0].Label);
    }

    // ── Scalar kinds return empty ─────────────────────────────────────────────

    [Theory]
    [InlineData(XmlValueType.Int)]
    [InlineData(XmlValueType.Float)]
    [InlineData(XmlValueType.FloatVector3)]
    [InlineData(XmlValueType.NameReference)] // no ObjectType set → no proposals
    public void GetProposals_ScalarOrUnconstrainedRef_ReturnsEmpty(XmlValueType valueType)
    {
        var sut = new StoryParamValueProposalProvider();
        // For NameReference with null ObjectType: no filter → empty
        var param = new ParamDefinition { Position = 0, ValueType = valueType };

        Assert.Empty(sut.GetProposals(param, "", GameIndex.Empty));
    }

    // ── Null param → empty ───────────────────────────────────────────────────

    [Fact]
    public void GetProposals_NullParam_ReturnsEmpty()
    {
        var sut = new StoryParamValueProposalProvider();

        Assert.Empty(sut.GetProposals(null, "", GameIndex.Empty));
    }
}