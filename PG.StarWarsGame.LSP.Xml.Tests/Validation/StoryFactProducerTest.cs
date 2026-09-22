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

    // ── Story dialog reference facts ─────────────────────────────────────────

    [Fact]
    public void Event_with_StoryDialog_and_chapter_emits_StoryDialogRefFact()
    {
        var sut = new StoryFactProducer(new EmptySchemaProvider());
        var xml = Xml("<Story_Dialog>Dialog_Mission_One</Story_Dialog><Story_Chapter>2</Story_Chapter>");

        var facts = sut.Produce(xml, "file:///test.xml").OfType<StoryDialogRefFact>().ToList();

        var f = Assert.Single(facts);
        Assert.Equal("Dialog_Mission_One", f.DialogName);
        Assert.Equal(2, f.Chapter);
        Assert.True(f.ChapterLine >= 0);
    }

    [Fact]
    public void Event_with_StoryDialog_without_chapter_emits_fact_with_null_chapter()
    {
        var sut = new StoryFactProducer(new EmptySchemaProvider());
        var xml = Xml("<Story_Dialog>Dialog_Mission_One</Story_Dialog>");

        var f = Assert.Single(sut.Produce(xml, "file:///test.xml").OfType<StoryDialogRefFact>());
        Assert.Null(f.Chapter);
        Assert.Equal(-1, f.ChapterLine);
    }

    [Fact]
    public void Event_without_StoryDialog_emits_no_StoryDialogRefFact()
    {
        var sut = new StoryFactProducer(new EmptySchemaProvider());
        var xml = Xml("<Event_Type>UNKNOWN</Event_Type>");

        Assert.Empty(sut.Produce(xml, "file:///test.xml").OfType<StoryDialogRefFact>());
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

    // ── Anchors: a fact marks the element that caused it, never column 0 of a line ────
    //
    // The squiggle has to sit on the thing to fix: a type problem on the type's value, a param
    // problem on the param's value, a MISSING param on the whole type element that demands it,
    // a dialog problem on the dialog's value and a chapter problem on the chapter's.

    private const string Anchored =
        "<StoryParser>\n" +
        "<Event Name=\"E\">\n" +
        "    <Event_Type>MY_EVENT</Event_Type>\n" +
        "    <Event_Param1>  42 </Event_Param1>\n" +
        "    <Reward_Type>MY_REWARD</Reward_Type>\n" +
        "    <Story_Dialog>Dialog_One</Story_Dialog>\n" +
        "    <Story_Chapter>2</Story_Chapter>\n" +
        "</Event>\n" +
        "</StoryParser>";

    private static StoryFactProducer AnchoredProducer()
    {
        return new StoryFactProducer(new SingleEventSchemaProvider(
            new EnumDefinition
            {
                Name = "StoryEventType", Kind = EnumKind.SchemaFixed,
                Values =
                [
                    new EnumValueDefinition
                    {
                        Name = "MY_EVENT",
                        Params =
                        [
                            new ParamDefinition { Position = 0, ValueType = XmlValueType.Int, Optional = false },
                            new ParamDefinition { Position = 1, ValueType = XmlValueType.Int, Optional = false }
                        ]
                    }
                ]
            },
            new EnumDefinition
            {
                Name = "StoryRewardType", Kind = EnumKind.SchemaFixed,
                Values =
                [
                    new EnumValueDefinition
                    {
                        Name = "MY_REWARD",
                        Params = [new ParamDefinition { Position = 0, ValueType = XmlValueType.Int, Optional = false }]
                    }
                ]
            }));
    }

    [Fact]
    public void Type_fact_marks_the_type_value()
    {
        var facts = AnchoredProducer().Produce(Anchored, "file:///test.xml").OfType<StoryEventFact>().ToList();

        var eventType = facts.Single(f => !f.IsReward);
        Assert.Equal((2, 16, 8), (eventType.Line, eventType.Column, eventType.Length));
        var rewardType = facts.Single(f => f.IsReward);
        Assert.Equal((4, 17, 9), (rewardType.Line, rewardType.Column, rewardType.Length));
    }

    [Fact]
    public void Param_value_fact_marks_the_trimmed_value()
    {
        var facts = AnchoredProducer().Produce(Anchored, "file:///test.xml").OfType<StoryParamFact>().ToList();

        var value = facts.Single(f => !f.IsReward && f.RawValue == "42");
        Assert.Equal((3, 20, 2), (value.Line, value.Column, value.Length));
    }

    [Fact]
    public void Missing_required_param_marks_the_whole_type_element_that_demands_it()
    {
        var facts = AnchoredProducer().Produce(Anchored, "file:///test.xml").OfType<StoryParamFact>().ToList();

        var eventSlot = facts.Single(f => !f.IsReward && f.SlotPosition == 1);
        Assert.Equal("", eventSlot.RawValue);
        Assert.Equal((2, 4, 33), (eventSlot.Line, eventSlot.Column, eventSlot.Length));
        var rewardSlot = facts.Single(f => f.IsReward && f.SlotPosition == 0);
        Assert.Equal("", rewardSlot.RawValue);
        Assert.Equal((4, 4, 36), (rewardSlot.Line, rewardSlot.Column, rewardSlot.Length));
    }

    [Fact]
    public void Dialog_fact_marks_the_dialog_value_and_the_chapter_value()
    {
        var f = Assert.Single(AnchoredProducer().Produce(Anchored, "file:///test.xml").OfType<StoryDialogRefFact>());

        Assert.Equal((5, 18, 10), (f.Line, f.Column, f.Length));
        Assert.Equal((6, 19, 1), (f.ChapterLine, f.ChapterColumn, f.ChapterLength));
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

    public SingleEventSchemaProvider(params EnumDefinition[] enumDefs)
    {
        _enums = new Dictionary<string, EnumDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var enumDef in enumDefs)
            _enums[enumDef.Name] = enumDef;
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