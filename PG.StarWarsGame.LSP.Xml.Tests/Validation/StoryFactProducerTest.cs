// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

public sealed class StoryFactProducerTest
{
    private static string Xml(string inner)
    {
        return $"<StoryParser><Event>{inner}</Event></StoryParser>";
    }

    private static ISchemaProvider SchemaWithEvent(
        string name, bool deprecated = false,
        Dictionary<string, string>? notes = null,
        ParamDefinition[]? paramDefs = null)
    {
        var value = new EnumValueDefinition
        {
            Name = name, Deprecated = deprecated,
            Notes = notes ?? new Dictionary<string, string>(),
            Params = paramDefs is { Length: > 0 } ? [.. paramDefs] : null
        };
        return new SingleEventSchemaProvider(new EnumDefinition
        {
            Name = "StoryEventType",
            Kind = EnumKind.SchemaFixed,
            Values = [value]
        });
    }

    private static ISchemaProvider SchemaWithReward(string name, ParamDefinition[]? paramDefs = null)
    {
        var value = new EnumValueDefinition
        {
            Name = name,
            Params = paramDefs is { Length: > 0 } ? [.. paramDefs] : null
        };
        return new SingleEventSchemaProvider(new EnumDefinition
        {
            Name = "StoryRewardType",
            Kind = EnumKind.SchemaFixed,
            Values = [value]
        });
    }

    // ── Event type facts ─────────────────────────────────────────────────────

    [Fact]
    public void Known_event_type_emits_StoryEventFact_with_def()
    {
        var sut = new StoryFactProducer(SchemaWithEvent("MY_EVENT"));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type>");
        var facts = sut.Produce(xml, "file:///test.xml").OfType<StoryEventFact>().ToList();
        var f = Assert.Single(facts);
        Assert.Equal("MY_EVENT", f.EventType);
        Assert.False(f.IsReward);
        Assert.NotNull(f.Def);
    }

    [Fact]
    public void Unknown_event_type_emits_StoryEventFact_with_null_def()
    {
        var sut = new StoryFactProducer(new EmptySchemaProvider());
        var xml = Xml("<Event_Type>UNKNOWN</Event_Type>");
        var facts = sut.Produce(xml, "file:///test.xml").OfType<StoryEventFact>().ToList();
        var f = Assert.Single(facts);
        Assert.Null(f.Def);
    }

    [Fact]
    public void Known_reward_type_emits_StoryEventFact_with_IsReward_true()
    {
        var sut = new StoryFactProducer(SchemaWithReward("MY_REWARD"));
        var xml = Xml("<Event_Type>X</Event_Type><Reward_Type>MY_REWARD</Reward_Type>");
        var facts = sut.Produce(xml, "file:///test.xml").OfType<StoryEventFact>().ToList();
        var rewardFact = facts.Single(f => f.IsReward);
        Assert.Equal("MY_REWARD", rewardFact.EventType);
        Assert.NotNull(rewardFact.Def);
    }

    // ── Param facts for occupied slots ───────────────────────────────────────

    [Fact]
    public void Occupied_param_slot_emits_StoryParamFact_with_value()
    {
        var sut = new StoryFactProducer(SchemaWithEvent("MY_EVENT",
            paramDefs: [new ParamDefinition { Position = 0, ValueType = XmlValueType.Int }]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1>42</Event_Param1>");
        var facts = sut.Produce(xml, "file:///test.xml").OfType<StoryParamFact>().ToList();
        var f = Assert.Single(facts);
        Assert.Equal("42", f.RawValue);
        Assert.Equal(0, f.SlotPosition);
        Assert.NotNull(f.Def);
    }

    [Fact]
    public void Empty_param_slot_does_not_emit_StoryParamFact_for_optional_param()
    {
        var sut = new StoryFactProducer(SchemaWithEvent("MY_EVENT",
            paramDefs: [new ParamDefinition { Position = 0, ValueType = XmlValueType.Int, Optional = true }]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type><Event_Param1></Event_Param1>");
        var paramFacts = sut.Produce(xml, "file:///test.xml").OfType<StoryParamFact>().ToList();
        Assert.Empty(paramFacts);
    }

    [Fact]
    public void Unconstrained_event_with_params_emits_no_StoryParamFacts()
    {
        var sut = new StoryFactProducer(new SingleEventSchemaProvider(new EnumDefinition
        {
            Name = "StoryEventType",
            Kind = EnumKind.SchemaFixed,
            Values = [new EnumValueDefinition { Name = "UNCONSTRAINED", Params = null }]
        }));
        var xml = Xml("<Event_Type>UNCONSTRAINED</Event_Type><Event_Param1>x</Event_Param1>");
        var paramFacts = sut.Produce(xml, "file:///test.xml").OfType<StoryParamFact>().ToList();
        Assert.Empty(paramFacts);
    }

    // ── Excess slot ──────────────────────────────────────────────────────────

    [Fact]
    public void Excess_param_slot_emits_StoryParamFact_with_null_def()
    {
        var sut = new StoryFactProducer(SchemaWithEvent("MY_EVENT",
            paramDefs: [new ParamDefinition { Position = 0, ValueType = XmlValueType.Int }]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type>" +
                      "<Event_Param1>1</Event_Param1>" +
                      "<Event_Param2>extra</Event_Param2>");
        var facts = sut.Produce(xml, "file:///test.xml").OfType<StoryParamFact>().ToList();
        var excess = facts.Single(f => f.SlotPosition == 1);
        Assert.Null(excess.Def);
        Assert.Equal("extra", excess.RawValue);
        Assert.Equal("MY_EVENT", excess.EventType);
    }

    // ── Required param missing ───────────────────────────────────────────────

    [Fact]
    public void Missing_required_param_emits_StoryParamFact_with_empty_RawValue()
    {
        var sut = new StoryFactProducer(SchemaWithEvent("MY_EVENT",
            paramDefs: [new ParamDefinition { Position = 0, ValueType = XmlValueType.Int, Optional = false }]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type>");
        var facts = sut.Produce(xml, "file:///test.xml").OfType<StoryParamFact>().ToList();
        var f = Assert.Single(facts);
        Assert.Equal("", f.RawValue);
        Assert.NotNull(f.Def);
        Assert.False(f.Def!.Optional);
    }

    [Fact]
    public void Missing_optional_param_emits_no_StoryParamFact()
    {
        var sut = new StoryFactProducer(SchemaWithEvent("MY_EVENT",
            paramDefs: [new ParamDefinition { Position = 0, ValueType = XmlValueType.Int, Optional = true }]));
        var xml = Xml("<Event_Type>MY_EVENT</Event_Type>");
        var facts = sut.Produce(xml, "file:///test.xml").OfType<StoryParamFact>().ToList();
        Assert.Empty(facts);
    }

    // ── Unknown event type → no params ──────────────────────────────────────

    [Fact]
    public void Unknown_event_type_emits_no_StoryParamFacts()
    {
        var sut = new StoryFactProducer(new EmptySchemaProvider());
        var xml = Xml("<Event_Type>UNKNOWN</Event_Type><Event_Param1>x</Event_Param1>");
        var paramFacts = sut.Produce(xml, "file:///test.xml").OfType<StoryParamFact>().ToList();
        Assert.Empty(paramFacts);
    }
}

file sealed class SingleEventSchemaProvider : ISchemaProvider
{
    private readonly Dictionary<string, EnumDefinition> _enums;

    public SingleEventSchemaProvider(EnumDefinition enumDef)
    {
        _enums = new Dictionary<string, EnumDefinition>(StringComparer.OrdinalIgnoreCase)
            { [enumDef.Name] = enumDef };
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