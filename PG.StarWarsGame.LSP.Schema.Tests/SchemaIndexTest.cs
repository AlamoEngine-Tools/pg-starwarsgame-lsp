// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

public sealed class SchemaIndexTest
{
    // ── helpers ────────────────────────────────────────────────────────────

    private static RawTagDefinition Tag(string name)
    {
        return new RawTagDefinition { Tag = name, ValueType = XmlValueType.Float };
    }

    private static GameObjectTypeDefinition Type(string name, string? nameTag = "Name")
    {
        return new GameObjectTypeDefinition { TypeName = name, NameTag = nameTag };
    }

    private static SchemaIndex Build(
        IEnumerable<(string type, IReadOnlyList<RawTagDefinition> tags)> tagsByType,
        IEnumerable<GameObjectTypeDefinition>? types = null,
        IEnumerable<RawEnumDefinition>? enums = null)
    {
        return new SchemaIndex(tagsByType, types ?? [], enums ?? []);
    }

    // ── Kinds ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetKind_ByNameCaseInsensitive_AndListedInAllKinds()
    {
        var planet = new ObjectKindDefinition { Kind = "Planet", Behaviors = ["PLANET"] };
        var index = new SchemaIndex([], [], [], kinds: [planet]);

        Assert.Same(planet, index.GetKind("planet"));
        Assert.Null(index.GetKind("StarBase"));
        Assert.Equal(new[] { "Planet" }, index.AllKinds.Select(k => k.Kind));
    }

    // ── GetTag ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetTag_ExistingTag_ReturnsDefinition()
    {
        var index = Build([("Foo", [Tag("Health")])]);
        Assert.Equal("Health", index.GetTag("Health")!.Tag);
    }

    [Fact]
    public void GetTag_CaseInsensitive()
    {
        var index = Build([("Foo", [Tag("Tactical_Health")])]);
        Assert.NotNull(index.GetTag("tactical_health"));
        Assert.NotNull(index.GetTag("TACTICAL_HEALTH"));
    }

    [Fact]
    public void GetTag_Unknown_ReturnsNull()
    {
        var index = Build([("Foo", [Tag("Health")])]);
        Assert.Null(index.GetTag("Missing"));
    }

    [Fact]
    public void GetTag_SameTagInTwoTypes_ReturnsFirst()
    {
        var a = new RawTagDefinition { Tag = "Text_ID", ValueType = XmlValueType.NameReference };
        var b = new RawTagDefinition { Tag = "Text_ID", ValueType = XmlValueType.Float };
        var index = Build([("TypeA", [a]), ("TypeB", [b])]);
        // First one encountered is TypeA's
        Assert.Equal(XmlValueType.NameReference, index.GetTag("Text_ID")!.ValueType);
    }

    [Fact]
    public void ResolveTag_VariantMode_FlowsToDefinition()
    {
        var raw = new RawTagDefinition
        {
            Tag = "Mass", ValueType = XmlValueType.Float, VariantMode = VariantMode.Merge
        };
        var index = Build([("Foo", [raw])]);

        Assert.Equal(VariantMode.Merge, index.GetTag("Mass")!.VariantMode);
    }

    [Fact]
    public void ResolveTag_NoVariantMode_DefaultsToReplace()
    {
        var index = Build([("Foo", [Tag("Health")])]);

        Assert.Equal(VariantMode.Replace, index.GetTag("Health")!.VariantMode);
    }

    // ── GetAllTagDefinitions ────────────────────────────────────────────────

    [Fact]
    public void GetAllTagDefinitions_SameNameAcrossTypes_ReturnsAll()
    {
        var index = Build([
            ("TypeA", [Tag("Text_ID")]),
            ("TypeB", [Tag("Text_ID")]),
            ("TypeC", [Tag("Text_ID")])
        ]);
        Assert.Equal(3, index.GetAllTagDefinitions("Text_ID").Count);
    }

    [Fact]
    public void GetAllTagDefinitions_Unknown_ReturnsEmpty()
    {
        var index = Build([("Foo", [Tag("Health")])]);
        Assert.Empty(index.GetAllTagDefinitions("Missing"));
    }

    [Fact]
    public void GetAllTagDefinitions_CaseInsensitive()
    {
        var index = Build([("Foo", [Tag("Text_ID")])]);
        Assert.Single(index.GetAllTagDefinitions("text_id"));
    }

    // ── GetTagsForType ──────────────────────────────────────────────────────

    [Fact]
    public void GetTagsForType_ReturnsOnlyThatType()
    {
        var index = Build([
            ("TypeA", [Tag("Alpha"), Tag("Beta")]),
            ("TypeB", [Tag("Gamma")])
        ]);
        Assert.Equal(2, index.GetTagsForType("TypeA").Count);
        Assert.Single(index.GetTagsForType("TypeB"));
    }

    [Fact]
    public void GetTagsForType_CaseInsensitive()
    {
        var index = Build([("GameObjectType", [Tag("Health"), Tag("Speed")])]);
        Assert.Equal(2, index.GetTagsForType("gameobjecttype").Count);
        Assert.Equal(2, index.GetTagsForType("GAMEOBJECTTYPE").Count);
    }

    [Fact]
    public void GetTagsForType_Unknown_ReturnsEmpty()
    {
        var index = Build([("Foo", [Tag("Health")])]);
        Assert.Empty(index.GetTagsForType("DoesNotExist"));
    }

    // ── GetObjectType ───────────────────────────────────────────────────────

    [Fact]
    public void GetObjectType_ExistingType_ReturnsDefinition()
    {
        var index = Build([], [Type("GameConstants", null)]);
        Assert.Equal("GameConstants", index.GetObjectType("GameConstants")!.TypeName);
    }

    [Fact]
    public void GetObjectType_CaseInsensitive()
    {
        var index = Build([], [Type("GameObjectType")]);
        Assert.NotNull(index.GetObjectType("gameobjecttype"));
        Assert.NotNull(index.GetObjectType("GAMEOBJECTTYPE"));
    }

    [Fact]
    public void GetObjectType_Unknown_ReturnsNull()
    {
        var index = Build([], [Type("Faction")]);
        Assert.Null(index.GetObjectType("Missing"));
    }

    // ── AllTags / AllObjectTypes ────────────────────────────────────────────

    [Fact]
    public void AllTags_AggregatesDistinctFirstDefinitions()
    {
        // 2 types × 2 unique tags each = 4, but Text_ID appears in both → 3 distinct
        var index = Build([
            ("TypeA", [Tag("Health"), Tag("Text_ID")]),
            ("TypeB", [Tag("Speed"), Tag("Text_ID")])
        ]);
        Assert.Equal(3, index.AllTags.Count);
    }

    [Fact]
    public void AllObjectTypes_ReturnsAllTypes()
    {
        var index = Build([], [Type("A"), Type("B"), Type("C")]);
        Assert.Equal(3, index.AllObjectTypes.Count);
    }

    // ── Empty singleton ─────────────────────────────────────────────────────

    [Fact]
    public void Empty_HasNoTagsOrTypes()
    {
        Assert.Empty(SchemaIndex.Empty.AllTags);
        Assert.Empty(SchemaIndex.Empty.AllObjectTypes);
        Assert.Null(SchemaIndex.Empty.GetTag("Anything"));
        Assert.Null(SchemaIndex.Empty.GetObjectType("Anything"));
    }

    // ── Resolution ──────────────────────────────────────────────────────────

    [Fact]
    public void SchemaIndex_ResolvesXmlObjectType()
    {
        var faction = Type("Faction");
        var raw = new RawTagDefinition
        {
            Tag = "Affiliation",
            ValueType = XmlValueType.NameReference,
            ReferenceKind = ReferenceKind.XmlObject,
            ReferenceType = "Faction"
        };

        var index = Build([("SomeType", [raw])], [faction]);

        Assert.Same(faction, index.GetTag("Affiliation")!.ObjectType);
        Assert.Null(index.GetTag("Affiliation")!.HardcodedSet);
        Assert.Null(index.GetTag("Affiliation")!.Enum);
    }

    [Fact]
    public void SchemaIndex_ResolvesHardcodedSet()
    {
        var set = new HardcodedReferenceSet { Name = "PlayerSide" };
        var raw = new RawTagDefinition
        {
            Tag = "Side",
            ValueType = XmlValueType.NameReference,
            ReferenceKind = ReferenceKind.HardcodedSet,
            ReferenceType = "PlayerSide"
        };

        var index = new SchemaIndex([("SomeType", [raw])], [], [], [set]);

        Assert.Same(set, index.GetTag("Side")!.HardcodedSet);
        Assert.Null(index.GetTag("Side")!.ObjectType);
        Assert.Null(index.GetTag("Side")!.Enum);
    }

    [Fact]
    public void SchemaIndex_ResolvesEnum()
    {
        var rawEnum = new RawEnumDefinition
        {
            Name = "SFXEventType",
            Values = []
        };
        var rawTag = new RawTagDefinition
        {
            Tag = "Event_Type",
            ValueType = XmlValueType.DynamicEnumValue,
            ReferenceKind = ReferenceKind.Enum,
            EnumName = "SFXEventType"
        };

        var index = Build([("SomeType", [rawTag])], enums: [rawEnum]);

        Assert.Equal("SFXEventType", index.GetTag("Event_Type")!.Enum?.Name);
        Assert.Null(index.GetTag("Event_Type")!.ObjectType);
        Assert.Null(index.GetTag("Event_Type")!.HardcodedSet);
    }

    [Fact]
    public void SchemaIndex_UnresolvableReference_LeavesPropertyNull()
    {
        var raw = new RawTagDefinition
        {
            Tag = "Unknown",
            ValueType = XmlValueType.NameReference,
            ReferenceKind = ReferenceKind.XmlObject,
            ReferenceType = "DoesNotExist"
        };

        var index = Build([("SomeType", [raw])]);

        Assert.Null(index.GetTag("Unknown")!.ObjectType);
    }

    [Fact]
    public void SchemaIndex_ResolvesEnumParamsInEnumValues()
    {
        var planetType = Type("Planet");
        var rawParam = new RawParamDefinition
        {
            Position = 0,
            ValueType = XmlValueType.NameReferenceList,
            ReferenceKind = ReferenceKind.XmlObject,
            ReferenceType = "Planet"
        };
        var rawEnumVal = new RawEnumValueDefinition { Name = "STORY_WATCH_PLANET", Params = [rawParam] };
        var rawEnum = new RawEnumDefinition { Name = "StoryEventType", Values = [rawEnumVal] };

        var index = Build([], [planetType], [rawEnum]);

        var enumDef = index.GetEnum("StoryEventType")!;
        var param = enumDef.Values.Single().Params!.Single();
        Assert.Same(planetType, param.ObjectType);
    }

    // The story enum files declare no referenceKind on their params: a DynamicEnumValue names its
    // enum and a NameReference its type, and that is the whole of the declaration. The index
    // resolves both from the name, or the enum params never complete or validate.
    [Fact]
    public void SchemaIndex_ResolvesParamEnumAndObjectType_FromTheirNamesAlone()
    {
        var planetType = Type("Planet");
        var rawEnum = new RawEnumDefinition
            { Name = "StoryEventFilter", Values = [new RawEnumValueDefinition { Name = "FILTER_NONE" }] };
        var enumParam = new RawParamDefinition
            { Position = 1, ValueType = XmlValueType.DynamicEnumValue, EnumName = "StoryEventFilter" };
        var objectParam = new RawParamDefinition
            { Position = 0, ValueType = XmlValueType.NameReferenceList, ReferenceType = "Planet" };
        var events = new RawEnumDefinition
        {
            Name = "StoryEventType",
            Values = [new RawEnumValueDefinition { Name = "STORY_ENTER", Params = [objectParam, enumParam] }]
        };

        // The referencing enum first: the file order must not decide whether the filter resolves
        // (StoryEventType sorts before StoryFlagCompareMethod, and STORY_FLAG's compare method
        // came back null for exactly that reason).
        var index = Build([], [planetType], [events, rawEnum]);

        var resolved = index.GetEnum("StoryEventType")!.Values.Single().Params!;
        Assert.Same(planetType, resolved.Single(p => p.Position == 0).ObjectType);
        Assert.Equal(ReferenceKind.XmlObject, resolved.Single(p => p.Position == 0).ReferenceKind);
        Assert.Equal("FILTER_NONE", Assert.Single(resolved.Single(p => p.Position == 1).Enum!.Values).Name);
        Assert.Equal(ReferenceKind.Enum, resolved.Single(p => p.Position == 1).ReferenceKind);
    }

    [Fact]
    public void SchemaIndex_ParamReferenceTypeName_IsPreservedEvenWithoutResolution()
    {
        // Story edge extraction is driven by the raw referenceType string (StoryEventName,
        // StoryFlag, StoryPlotFile) - these are not types.yaml object types, so the name must
        // survive resolution even though ObjectType stays null.
        var rawParam = new RawParamDefinition
        {
            Position = 0,
            ValueType = XmlValueType.NameReference,
            ReferenceType = "StoryEventName"
        };
        var rawEnumVal = new RawEnumValueDefinition { Name = "TRIGGER_EVENT", Params = [rawParam] };
        var rawEnum = new RawEnumDefinition { Name = "StoryRewardType", Values = [rawEnumVal] };

        var index = Build([], enums: [rawEnum]);

        var param = index.GetEnum("StoryRewardType")!.Values.Single().Params!.Single();
        Assert.Equal("StoryEventName", param.ReferenceTypeName);
        Assert.Null(param.ObjectType);
    }
}