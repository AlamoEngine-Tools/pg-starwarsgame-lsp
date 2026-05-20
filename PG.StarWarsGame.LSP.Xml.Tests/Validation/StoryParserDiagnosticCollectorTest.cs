// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

file sealed class StubSchemaProvider : ISchemaProvider
{
    private readonly Dictionary<string, EnumDefinition> _enums;

    public StubSchemaProvider(params EnumDefinition[] enums)
    {
        _enums = enums.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
    }

    public EnumDefinition? GetEnum(string name) => _enums.GetValueOrDefault(name);
    public IReadOnlyList<EnumDefinition> AllEnums => [.. _enums.Values];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public XmlTagDefinition? GetTag(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
    public GameObjectTypeDefinition? GetObjectType(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];
    public event EventHandler? SchemaRefreshed { add { } remove { } }
}

public sealed class StoryParserDiagnosticCollectorTest
{
    // ── XML helpers ──────────────────────────────────────────────────────────

    private static string Xml(string inner) =>
        $"<StoryParser><Event>{inner}</Event></StoryParser>";

    // ── Schema builder helpers ───────────────────────────────────────────────

    private static ISchemaProvider SchemaWithEvent(
        string eventName,
        bool deprecated = false,
        Dictionary<string, string>? notes = null,
        ParamDefinition[]? paramDefs = null)
    {
        var value = new EnumValueDefinition
        {
            Name = eventName,
            Deprecated = deprecated,
            Notes = notes ?? new Dictionary<string, string>(),
            Params = paramDefs is { Length: > 0 } ? [.. paramDefs] : null
        };
        return new StubSchemaProvider(new EnumDefinition
        {
            Name = "StoryEventType",
            Kind = EnumKind.SchemaFixed,
            Values = [value]
        });
    }

    private static ISchemaProvider SchemaWithReward(
        string rewardName,
        bool deprecated = false,
        Dictionary<string, string>? notes = null,
        ParamDefinition[]? paramDefs = null)
    {
        var value = new EnumValueDefinition
        {
            Name = rewardName,
            Deprecated = deprecated,
            Notes = notes ?? new Dictionary<string, string>(),
            Params = paramDefs is { Length: > 0 } ? [.. paramDefs] : null
        };
        return new StubSchemaProvider(new EnumDefinition
        {
            Name = "StoryRewardType",
            Kind = EnumKind.SchemaFixed,
            Values = [value]
        });
    }

    private static ISchemaProvider SchemaWithTwoEventTypes(
        string eventA, ParamDefinition[] paramsA,
        string eventB, ParamDefinition[] paramsB,
        params EnumDefinition[] extra)
    {
        var eventEnum = new EnumDefinition
        {
            Name = "StoryEventType",
            Kind = EnumKind.SchemaFixed,
            Values =
            [
                new EnumValueDefinition { Name = eventA, Params = paramsA },
                new EnumValueDefinition { Name = eventB, Params = paramsB }
            ]
        };
        return new StubSchemaProvider([eventEnum, .. extra]);
    }

    private static ISchemaProvider SchemaWithTwoRewardTypes(
        string rewardA, ParamDefinition[] paramsA,
        string rewardB, ParamDefinition[] paramsB)
    {
        return new StubSchemaProvider(new EnumDefinition
        {
            Name = "StoryRewardType",
            Kind = EnumKind.SchemaFixed,
            Values =
            [
                new EnumValueDefinition { Name = rewardA, Params = paramsA },
                new EnumValueDefinition { Name = rewardB, Params = paramsB }
            ]
        });
    }

    // ── Param definition factories ───────────────────────────────────────────

    private static ParamDefinition IntParam(int position, bool optional = false) => new()
    {
        Position = position,
        ValueType = XmlValueType.Int,
        Optional = optional
    };

    private static ParamDefinition BoolParam(int position, bool optional = false) => new()
    {
        Position = position,
        ValueType = XmlValueType.Boolean,
        Optional = optional
    };

    private static ParamDefinition EnumParam(int position, string enumName, bool optional = false) => new()
    {
        Position = position,
        ValueType = XmlValueType.DynamicEnumValue,
        EnumName = enumName,
        Optional = optional
    };

    private static ParamDefinition RefParam(int position, string referenceType, bool optional = false) => new()
    {
        Position = position,
        ValueType = XmlValueType.NameReference,
        ReferenceType = referenceType,
        Optional = optional
    };

    private static ParamDefinition RefListParam(int position, string referenceType, bool optional = false) => new()
    {
        Position = position,
        ValueType = XmlValueType.NameReferenceList,
        ReferenceType = referenceType,
        Optional = optional
    };

    private static EnumDefinition ValueEnum(string name, params string[] values) => new()
    {
        Name = name,
        Kind = EnumKind.SchemaFixed,
        Values = [.. values.Select(v => new EnumValueDefinition { Name = v })]
    };

    // ── Game index helpers ───────────────────────────────────────────────────

    private static GameIndex IndexWithSymbols(params (string id, string typeName)[] symbols)
    {
        var dict = ImmutableDictionary.CreateRange(
            StringComparer.OrdinalIgnoreCase,
            symbols.Select(s => KeyValuePair.Create(s.id,
                new GameSymbol(s.id, GameSymbolKind.XmlObject, s.typeName, new UnknownOrigin("test"), null))));
        return GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with { Symbols = dict }
        };
    }

    // ── Int param validation ─────────────────────────────────────────────────

    [Fact]
    public void Collect_EventWithSchemaParams_ValidatesIntParam()
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", paramDefs: [IntParam(0)]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1>not_an_int</Event_Param1>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("not_an_int"));
    }

    [Fact]
    public void Collect_EventWithSchemaParams_ValidatesIntParam_NoWarningForValidInt()
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", paramDefs: [IntParam(0)]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1>42</Event_Param1>");

        Assert.Empty(sut.Collect(xml, GameIndex.Empty));
    }

    // ── Enum param validation ────────────────────────────────────────────────

    [Fact]
    public void Collect_EventWithSchemaParams_ValidatesEnumParam()
    {
        var provider = new StubSchemaProvider(
            new EnumDefinition
            {
                Name = "StoryEventType",
                Kind = EnumKind.SchemaFixed,
                Values = [new EnumValueDefinition { Name = "MY_EVENT", Params = [EnumParam(0, "ColorEnum")] }]
            },
            ValueEnum("ColorEnum", "RED", "GREEN", "BLUE"));
        var sut = new StoryParserDiagnosticCollector(provider);

        var badXml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1>YELLOW</Event_Param1>");
        Assert.Contains(sut.Collect(badXml, GameIndex.Empty),
            d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("YELLOW"));

        var goodXml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1>RED</Event_Param1>");
        Assert.Empty(sut.Collect(goodXml, GameIndex.Empty));
    }

    [Fact]
    public void Collect_EventWithSchemaParams_ValidatesEnumParam_CaseInsensitive()
    {
        var provider = new StubSchemaProvider(
            new EnumDefinition
            {
                Name = "StoryEventType",
                Kind = EnumKind.SchemaFixed,
                Values = [new EnumValueDefinition { Name = "MY_EVENT", Params = [EnumParam(0, "ColorEnum")] }]
            },
            ValueEnum("ColorEnum", "RED", "GREEN"));
        var sut = new StoryParserDiagnosticCollector(provider);

        var xml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1>red</Event_Param1>");
        Assert.Empty(sut.Collect(xml, GameIndex.Empty));
    }

    // ── Enum list validation (space-separated tokens) ────────────────────────

    [Fact]
    public void Collect_EventWithSchemaParams_ValidatesEnumListParam_AllValidTokens()
    {
        var provider = new StubSchemaProvider(
            new EnumDefinition
            {
                Name = "StoryEventType",
                Kind = EnumKind.SchemaFixed,
                Values = [new EnumValueDefinition { Name = "MY_EVENT", Params = [EnumParam(0, "Triggers")] }]
            },
            ValueEnum("Triggers", "END_SETUP", "CLICK", "CLOSE_DIALOG"));
        var sut = new StoryParserDiagnosticCollector(provider);

        var xml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1>END_SETUP CLICK</Event_Param1>");
        Assert.Empty(sut.Collect(xml, GameIndex.Empty));
    }

    [Fact]
    public void Collect_EventWithSchemaParams_ValidatesEnumListParam_InvalidToken()
    {
        var provider = new StubSchemaProvider(
            new EnumDefinition
            {
                Name = "StoryEventType",
                Kind = EnumKind.SchemaFixed,
                Values = [new EnumValueDefinition { Name = "MY_EVENT", Params = [EnumParam(0, "Triggers")] }]
            },
            ValueEnum("Triggers", "END_SETUP", "CLICK"));
        var sut = new StoryParserDiagnosticCollector(provider);

        var xml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1>END_SETUP INVALID_TOKEN</Event_Param1>");
        Assert.Contains(sut.Collect(xml, GameIndex.Empty),
            d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("INVALID_TOKEN"));
    }

    // ── Boolean param validation ─────────────────────────────────────────────

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("TRUE")]
    [InlineData("FALSE")]
    public void Collect_EventWithSchemaParams_ValidatesBooleanParam_AcceptsValidValues(string value)
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", paramDefs: [BoolParam(0)]));
        var xml = Xml($"<Event_Type>MY_EVENT</Event_Type><Event_Param1>{value}</Event_Param1>");

        Assert.DoesNotContain(sut.Collect(xml, GameIndex.Empty), d => d.Message.Contains(value));
    }

    [Fact]
    public void Collect_EventWithSchemaParams_ValidatesBooleanParam_RejectsInvalid()
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", paramDefs: [BoolParam(0)]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1>yes</Event_Param1>");

        Assert.Contains(sut.Collect(xml, GameIndex.Empty),
            d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("yes"));
    }

    // ── Reference param validation ───────────────────────────────────────────

    [Fact]
    public void Collect_EventWithSchemaParams_ValidatesReferenceParam_ResolvedProducesNoDiag()
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", paramDefs: [RefParam(0, "Planet")]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1>Coruscant</Event_Param1>");

        Assert.Empty(sut.Collect(xml, IndexWithSymbols(("Coruscant", "Planet"))));
    }

    [Fact]
    public void Collect_EventWithSchemaParams_ValidatesReferenceParam_UnresolvedProducesWarning()
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", paramDefs: [RefParam(0, "Planet")]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1>NotAPlanet</Event_Param1>");

        Assert.Contains(sut.Collect(xml, GameIndex.Empty),
            d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("NotAPlanet"));
    }

    [Fact]
    public void Collect_EventWithSchemaParams_ValidatesReferenceListParam()
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", paramDefs: [RefListParam(0, "GameObjectType")]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type>" +
                      "<Event_Param1>X_Wing TIE_Fighter Missing_Unit</Event_Param1>");
        var index = IndexWithSymbols(("X_Wing", "GameObjectType"), ("TIE_Fighter", "GameObjectType"));

        var diags = sut.Collect(xml, index);
        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("Missing_Unit"));
        Assert.DoesNotContain(diags, d => d.Message.Contains("X_Wing"));
        Assert.DoesNotContain(diags, d => d.Message.Contains("TIE_Fighter"));
    }

    // ── Unconstrained event (Params == null) ─────────────────────────────────

    [Fact]
    public void Collect_EventWithNullParams_AllowsAnyParamSlot()
    {
        var provider = new StubSchemaProvider(new EnumDefinition
        {
            Name = "StoryEventType",
            Kind = EnumKind.SchemaFixed,
            Values = [new EnumValueDefinition { Name = "UNCONSTRAINED", Params = null }]
        });
        var sut = new StoryParserDiagnosticCollector(provider);
        var xml = Xml("<Event_Type>UNCONSTRAINED</Event_Type>" +
                      "<Event_Param1>anything</Event_Param1>" +
                      "<Event_Param7>more_stuff</Event_Param7>");

        Assert.Empty(sut.Collect(xml, GameIndex.Empty));
    }

    // ── Excess / unused params ───────────────────────────────────────────────

    [Fact]
    public void Collect_EventWithDefinedParams_WarnsOnExcessParam()
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", paramDefs: [IntParam(0)]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type>" +
                      "<Event_Param1>1</Event_Param1>" +
                      "<Event_Param2>extra</Event_Param2>");

        var diags = sut.Collect(xml, GameIndex.Empty);
        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("Event_Param2") &&
                                     d.Message.Contains("MY_EVENT"));
    }

    [Fact]
    public void Collect_EventWithGapParams_WarnsOnParamBeyondMaxPosition()
    {
        // Positions 0 and 2 are defined; position 1 is a gap; position 3+ is excess
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", paramDefs: [IntParam(0), IntParam(2)]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type>" +
                      "<Event_Param1>1</Event_Param1>" +
                      "<Event_Param3>2</Event_Param3>" +
                      "<Event_Param4>extra</Event_Param4>");

        var diags = sut.Collect(xml, GameIndex.Empty);
        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("Event_Param4"));
        Assert.DoesNotContain(diags, d => d.Message.Contains("Event_Param2"));
    }

    // ── Missing required params ──────────────────────────────────────────────

    [Fact]
    public void Collect_EventWithDefinedParams_WarnsOnMissingRequired()
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", paramDefs: [IntParam(0, optional: false)]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);
        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("MY_EVENT") &&
                                     d.Message.Contains("Event_Param1"));
    }

    [Fact]
    public void Collect_EventWithDefinedParams_NoWarningForAbsentOptionalParam()
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", paramDefs: [IntParam(0, optional: true)]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type>");

        Assert.Empty(sut.Collect(xml, GameIndex.Empty));
    }

    // ── Notes → Hint diagnostics ─────────────────────────────────────────────

    [Fact]
    public void Collect_EventWithNotes_EmitsHintOnEventTypeNode()
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", notes: new() { ["en"] = "Never used in vanilla." }));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);
        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Hint &&
                                     d.Message.Contains("Never used in vanilla."));
    }

    [Fact]
    public void Collect_RewardWithNotes_EmitsHintOnRewardTypeNode()
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithReward("MY_REWARD", notes: new() { ["en"] = "Causes crashes." }));
        var xml = Xml("<Event_Type>ANYTHING</Event_Type><Reward_Type>MY_REWARD</Reward_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);
        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Hint &&
                                     d.Message.Contains("Causes crashes."));
    }

    [Fact]
    public void Collect_ParamDefinitionWithNotes_EmitsHintOnParamNode()
    {
        var paramWithNote = new ParamDefinition
        {
            Position = 0,
            ValueType = XmlValueType.Int,
            Optional = true,
            Notes = new Dictionary<string, string> { ["en"] = "Param note here." }
        };
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", paramDefs: [paramWithNote]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1>5</Event_Param1>");

        var diags = sut.Collect(xml, GameIndex.Empty);
        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Hint &&
                                     d.Message.Contains("Param note here."));
    }

    // ── Deprecated event/reward ──────────────────────────────────────────────

    [Fact]
    public void Collect_DeprecatedEvent_EmitsWarning()
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("OLD_EVENT", deprecated: true));
        var xml = Xml("<Event_Type>OLD_EVENT</Event_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);
        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("OLD_EVENT") &&
                                     d.Message.Contains("deprecated", StringComparison.OrdinalIgnoreCase));
    }

    // ── Event type changed — same param slot, different type semantics ────────

    [Fact]
    public void Collect_EventTypeChanged_SameParam1_RevalidatesAgainstNewType()
    {
        // EVENT_A: Param1 = DynamicEnumValue (ColorEnum) — "RED" is valid
        // EVENT_B: Param1 = Int — "RED" is invalid
        var provider = new StubSchemaProvider(
            new EnumDefinition
            {
                Name = "StoryEventType",
                Kind = EnumKind.SchemaFixed,
                Values =
                [
                    new EnumValueDefinition { Name = "EVENT_A", Params = [EnumParam(0, "ColorEnum")] },
                    new EnumValueDefinition { Name = "EVENT_B", Params = [IntParam(0)] }
                ]
            },
            ValueEnum("ColorEnum", "RED", "GREEN"));
        var sut = new StoryParserDiagnosticCollector(provider);

        // With EVENT_A: "RED" is a valid ColorEnum → no warning
        var xmlA = Xml("<Event_Type>EVENT_A</Event_Type><Event_Param1>RED</Event_Param1>");
        Assert.Empty(sut.Collect(xmlA, GameIndex.Empty));

        // With EVENT_B: "RED" is not a valid Int → warning
        var xmlB = Xml("<Event_Type>EVENT_B</Event_Type><Event_Param1>RED</Event_Param1>");
        Assert.Contains(sut.Collect(xmlB, GameIndex.Empty),
            d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("RED"));
    }

    [Fact]
    public void Collect_EventTypeChanged_ParamValidForOldTypeNotNew_WarnsUnused()
    {
        // EVENT_A has 3 params; EVENT_B has only 1
        // Document uses EVENT_B with Event_Param2, Event_Param3 → "not used by EVENT_B"
        var sut = new StoryParserDiagnosticCollector(new StubSchemaProvider(new EnumDefinition
        {
            Name = "StoryEventType",
            Kind = EnumKind.SchemaFixed,
            Values =
            [
                new EnumValueDefinition { Name = "EVENT_A", Params = [IntParam(0), IntParam(1), IntParam(2)] },
                new EnumValueDefinition { Name = "EVENT_B", Params = [IntParam(0)] }
            ]
        }));

        var xml = Xml("<Event_Type>EVENT_B</Event_Type>" +
                      "<Event_Param1>1</Event_Param1>" +
                      "<Event_Param2>2</Event_Param2>" +
                      "<Event_Param3>3</Event_Param3>");

        var diags = sut.Collect(xml, GameIndex.Empty);
        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("Event_Param2") && d.Message.Contains("EVENT_B"));
        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("Event_Param3") && d.Message.Contains("EVENT_B"));
    }

    [Fact]
    public void Collect_EventTypeChanged_ParamValidForNewTypeNotOld_NoError()
    {
        // EVENT_A: Param1 = Int. EVENT_B: Param1 = NameReference("Planet").
        // With EVENT_B and a valid planet → no error
        var sut = new StoryParserDiagnosticCollector(new StubSchemaProvider(new EnumDefinition
        {
            Name = "StoryEventType",
            Kind = EnumKind.SchemaFixed,
            Values =
            [
                new EnumValueDefinition { Name = "EVENT_A", Params = [IntParam(0)] },
                new EnumValueDefinition { Name = "EVENT_B", Params = [RefParam(0, "Planet")] }
            ]
        }));

        var xml = Xml("<Event_Type>EVENT_B</Event_Type><Event_Param1>Coruscant</Event_Param1>");
        Assert.Empty(sut.Collect(xml, IndexWithSymbols(("Coruscant", "Planet"))));
    }

    [Fact]
    public void Collect_RewardTypeChanged_SameParam_RevalidatesAgainstNewType()
    {
        // REWARD_A: Param1 = Boolean — "true" is valid
        // REWARD_B: Param1 = Int — "true" is invalid
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithTwoRewardTypes("REWARD_A", [BoolParam(0)], "REWARD_B", [IntParam(0)]));

        var xml = Xml("<Event_Type>ANY</Event_Type><Reward_Type>REWARD_B</Reward_Type>" +
                      "<Reward_Param1>true</Reward_Param1>");

        Assert.Contains(sut.Collect(xml, GameIndex.Empty),
            d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("true"));
    }

    // ── Unknown types produce no story diagnostics ───────────────────────────

    [Fact]
    public void Collect_UnknownEventType_ProducesNoStoryDiagnostics()
    {
        var sut = new StoryParserDiagnosticCollector(new StubSchemaProvider());
        var xml = Xml("<Event_Type>STORY_NOT_REAL</Event_Type><Event_Param1>something</Event_Param1>");

        Assert.Empty(sut.Collect(xml, GameIndex.Empty));
    }

    [Fact]
    public void Collect_UnknownRewardType_ProducesNoStoryDiagnostics()
    {
        var sut = new StoryParserDiagnosticCollector(new StubSchemaProvider());
        var xml = Xml("<Event_Type>ANY</Event_Type><Reward_Type>NOT_A_REWARD</Reward_Type>" +
                      "<Reward_Param1>x</Reward_Param1>");

        Assert.Empty(sut.Collect(xml, GameIndex.Empty));
    }

    // ── Empty param value ────────────────────────────────────────────────────

    [Fact]
    public void Collect_EmptyParamValue_IsNotTypeValidated()
    {
        var sut = new StoryParserDiagnosticCollector(
            SchemaWithEvent("MY_EVENT", paramDefs: [IntParam(0, optional: true)]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1></Event_Param1>");

        var diags = sut.Collect(xml, GameIndex.Empty);
        Assert.All(diags, d => Assert.DoesNotContain("is not a valid", d.Message));
    }
}
