// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion.Strategies;

public sealed class TupleValueCompletionStrategyTest
{
    private static TagValueCompletionContext Ctx(
        XmlTagDefinition tagDef, int tupleSlotIndex, string partialValue = "",
        ISchemaProvider? schema = null)
    {
        var doc = XmlUtility.CreateHtmlDocument("<Root><Foo>x</Foo></Root>");
        var node = doc.DocumentNode.SelectSingleNode("//Foo")!;
        return new TagValueCompletionContext(
            "file:///test.xml", GameIndex.Empty, schema ?? new FakeSchemaProvider(), doc, node, "Foo", 2,
            tagDef, partialValue, 0, 0, false, null, 0, tupleSlotIndex);
    }

    private static XmlTagDefinition Tag(string name, XmlValueType valueType, TagValidationOverride? over = null)
    {
        return new XmlTagDefinition { Tag = name, ValueType = valueType, ValidationOverride = over };
    }

    // ── InaccuracyMap ──────────────────────────────────────────────────────────

    [Fact]
    public void InaccuracyMap_Slot0_QueriesGameObjectCategoryTypeEnum()
    {
        var schema = new FakeSchemaProvider();
        schema.AddEnum(new EnumDefinition
        {
            Name = "GameObjectCategoryType", Kind = EnumKind.DynamicXml,
            Values = [new EnumValueDefinition { Name = "Bomber" }]
        });
        var proposals = new CapturingProposalRegistry();
        var strategy = new TupleValueCompletionStrategy(schema, proposals, new CapturingCompletionRegistry());

        var result = strategy.Handle(Ctx(Tag("X", XmlValueType.InaccuracyMap), 0, "Bom", schema)).ToList();

        Assert.Equal(XmlValueType.DynamicEnumValue, proposals.LastValueType);
        Assert.Equal("GameObjectCategoryType", proposals.LastTag?.Enum?.Name);
        Assert.Single(result);
        Assert.Equal("Bomber", result[0].Label);
    }

    [Fact]
    public void InaccuracyMap_Slot1_HasNoCompletionSource()
    {
        var schema = new FakeSchemaProvider();
        var strategy = new TupleValueCompletionStrategy(
            schema, new CapturingProposalRegistry(), new CapturingCompletionRegistry());

        Assert.Empty(strategy.Handle(Ctx(Tag("X", XmlValueType.InaccuracyMap), 1, "", schema)));
    }

    // ── HardPointSfxMap ────────────────────────────────────────────────────────

    [Fact]
    public void HardPointSfxMap_Slot0_QueriesHardPointTypeEnum()
    {
        var schema = new FakeSchemaProvider();
        schema.AddEnum(new EnumDefinition
        {
            Name = "HardPointType", Kind = EnumKind.SchemaFixed,
            Values = [new EnumValueDefinition { Name = "HARD_POINT_WEAPON_LASER" }]
        });
        var proposals = new CapturingProposalRegistry();
        var strategy = new TupleValueCompletionStrategy(schema, proposals, new CapturingCompletionRegistry());

        var result = strategy.Handle(Ctx(Tag("X", XmlValueType.HardPointSfxMap), 0, "HARD", schema)).ToList();

        Assert.Equal(XmlValueType.DynamicEnumValue, proposals.LastValueType);
        Assert.Equal("HardPointType", proposals.LastTag?.Enum?.Name);
        Assert.Equal("HARD", proposals.LastPartialValue);
        Assert.Single(result);
        Assert.Equal("HARD_POINT_WEAPON_LASER", result[0].Label);
    }

    [Fact]
    public void HardPointSfxMap_Slot1_QueriesSFXEventReference()
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);

        strategy.Handle(Ctx(Tag("X", XmlValueType.HardPointSfxMap), 1, "SFX_")).ToList();

        Assert.Equal(ReferenceKind.XmlObject, completion.LastTag?.ReferenceKind);
        Assert.Equal("SFXEvent", completion.LastTag?.ObjectType?.TypeName);
        Assert.Equal("SFX_", completion.LastPartialValue);
    }

    // ── AbilitySfxMap ──────────────────────────────────────────────────────────

    [Fact]
    public void AbilitySfxMap_Slot0_QueriesAbilityTypeHardcodedSet()
    {
        var schema = new FakeSchemaProvider();
        schema.AddHardcodedSet(new HardcodedReferenceSet { Name = "AbilityType" });
        var completion = new CapturingCompletionRegistry();
        var strategy = new TupleValueCompletionStrategy(schema, new CapturingProposalRegistry(), completion);

        strategy.Handle(Ctx(Tag("X", XmlValueType.AbilitySfxMap), 0)).ToList();

        Assert.Equal(ReferenceKind.HardcodedSet, completion.LastTag?.ReferenceKind);
        Assert.Equal("AbilityType", completion.LastTag?.HardcodedSet?.Name);
    }

    [Fact]
    public void AbilitySfxMap_Slot1_QueriesSFXEventReference()
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);

        strategy.Handle(Ctx(Tag("X", XmlValueType.AbilitySfxMap), 1)).ToList();

        Assert.Equal("SFXEvent", completion.LastTag?.ObjectType?.TypeName);
    }

    // ── ConditionalSfxEvent ────────────────────────────────────────────────────

    [Fact]
    public void ConditionalSfxEvent_Slot0_ReturnsNoCompletions()
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);

        var result = strategy.Handle(Ctx(Tag("X", XmlValueType.ConditionalSfxEvent), 0)).ToList();

        Assert.Empty(result);
        Assert.Null(completion.LastTag);
    }

    [Fact]
    public void ConditionalSfxEvent_Slot1_QueriesSFXEventReference()
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);

        strategy.Handle(Ctx(Tag("X", XmlValueType.ConditionalSfxEvent), 1)).ToList();

        Assert.Equal("SFXEvent", completion.LastTag?.ObjectType?.TypeName);
    }

    // ── UnitSpawnTable ─────────────────────────────────────────────────────────

    [Fact]
    public void UnitSpawnTable_Slot0_QueriesGameObjectTypeWildcardReference()
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);

        strategy.Handle(Ctx(Tag("X", XmlValueType.UnitSpawnTable), 0)).ToList();

        Assert.Equal("GameObjectType", completion.LastTag?.ObjectType?.TypeName);
    }

    [Fact]
    public void UnitSpawnTable_Slot1_ReturnsNoCompletions()
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);

        var result = strategy.Handle(Ctx(Tag("X", XmlValueType.UnitSpawnTable), 1)).ToList();

        Assert.Empty(result);
        Assert.Null(completion.LastTag);
    }

    // ── AbilityModMultiplier ───────────────────────────────────────────────────

    [Fact]
    public void AbilityModMultiplier_Slot0_QueriesAbilityMultiplierTypeEnum()
    {
        var schema = new FakeSchemaProvider();
        schema.AddEnum(new EnumDefinition
        {
            Name = "AbilityMultiplierType", Kind = EnumKind.SchemaFixed, Values = []
        });
        var proposals = new CapturingProposalRegistry();
        var strategy = new TupleValueCompletionStrategy(schema, proposals, new CapturingCompletionRegistry());

        strategy.Handle(Ctx(Tag("X", XmlValueType.AbilityModMultiplier), 0, schema: schema)).ToList();

        Assert.Equal("AbilityMultiplierType", proposals.LastTag?.Enum?.Name);
    }

    [Fact]
    public void AbilityModMultiplier_Slot1_ReturnsNoCompletions()
    {
        var completion = new CapturingCompletionRegistry();
        var proposals = new CapturingProposalRegistry();
        var strategy = new TupleValueCompletionStrategy(new FakeSchemaProvider(), proposals, completion);

        var result = strategy.Handle(Ctx(Tag("X", XmlValueType.AbilityModMultiplier), 1)).ToList();

        Assert.Empty(result);
        Assert.Null(completion.LastTag);
        Assert.Null(proposals.LastTag);
    }

    // ── TupleList (context-name-pair / context-name-list) ─────────────────────

    [Fact]
    public void TupleList_ContextNamePair_Slot0_ReturnsNoCompletions()
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);
        var tag = Tag("Music_Event_List_Ambient", XmlValueType.TupleList,
            new TagValidationOverride { ValidationId = "context-name-pair" });

        var result = strategy.Handle(Ctx(tag, 0)).ToList();

        Assert.Empty(result);
        Assert.Null(completion.LastTag);
    }

    [Fact]
    public void TupleList_ContextNamePair_Slot1_QueriesMusicEventReference()
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);
        var tag = Tag("Music_Event_List_Ambient", XmlValueType.TupleList,
            new TagValidationOverride { ValidationId = "context-name-pair" });

        strategy.Handle(Ctx(tag, 1)).ToList();

        Assert.Equal("MusicEvent", completion.LastTag?.ObjectType?.TypeName);
    }

    [Fact]
    public void TupleList_ContextNameList_ReturnsNoCompletions()
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);
        var tag = Tag("Land_Terrain_Model_Mapping", XmlValueType.TupleList,
            new TagValidationOverride { ValidationId = "context-name-list" });

        var result0 = strategy.Handle(Ctx(tag, 0)).ToList();
        var result1 = strategy.Handle(Ctx(tag, 1)).ToList();

        Assert.Empty(result0);
        Assert.Empty(result1);
        Assert.Null(completion.LastTag);
    }

    [Fact]
    public void TupleList_NoValidationOverride_ReturnsNoCompletions()
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);
        var tag = Tag("Music_Events", XmlValueType.TupleList);

        var result = strategy.Handle(Ctx(tag, 0)).ToList();

        Assert.Empty(result);
    }

    // ── gating ─────────────────────────────────────────────────────────────────

    [Fact]
    public void StoryParamContext_ReturnsNoCompletions()
    {
        var strategy = new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(),
            new CapturingCompletionRegistry());
        var doc = XmlUtility.CreateHtmlDocument("<Root><Foo>x</Foo></Root>");
        var node = doc.DocumentNode.SelectSingleNode("//Foo")!;
        var ctx = new TagValueCompletionContext(
            "file:///test.xml", GameIndex.Empty, new FakeSchemaProvider(), doc, node, "Foo", 2,
            Tag("X", XmlValueType.HardPointSfxMap), "", 0, 0, true, "Event", 0);

        Assert.Empty(strategy.Handle(ctx));
    }

    [Fact]
    public void NonTupleValueType_ReturnsNoCompletions()
    {
        var strategy = new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(),
            new CapturingCompletionRegistry());

        Assert.Empty(strategy.Handle(Ctx(Tag("X", XmlValueType.Float), 0)));
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeSchemaProvider : ISchemaProvider
    {
        private readonly Dictionary<string, EnumDefinition> _enums = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HardcodedReferenceSet> _sets = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [.. _enums.Values];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [.. _sets.Values];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public XmlTagDefinition? GetTag(string tagName)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string enumName)
        {
            return _enums.GetValueOrDefault(enumName);
        }

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public void AddEnum(EnumDefinition enumDef)
        {
            _enums[enumDef.Name] = enumDef;
        }

        public void AddHardcodedSet(HardcodedReferenceSet set)
        {
            _sets[set.Name] = set;
        }
    }

    private sealed class CapturingProposalRegistry : IXmlValueProposalRegistry
    {
        public XmlValueType? LastValueType { get; private set; }
        public XmlTagDefinition? LastTag { get; private set; }
        public string? LastPartialValue { get; private set; }

        public IReadOnlyList<ValueProposal> GetProposals(XmlValueType valueType, XmlTagDefinition tag,
            string partialValue)
        {
            LastValueType = valueType;
            LastTag = tag;
            LastPartialValue = partialValue;
            return tag.Enum?.Values
                .Where(v => partialValue.Length == 0 ||
                            v.Name.StartsWith(partialValue, StringComparison.OrdinalIgnoreCase))
                .Select(v => new ValueProposal { Label = v.Name })
                .ToList() ?? [];
        }
    }

    private sealed class CapturingCompletionRegistry : IXmlCompletionRegistry
    {
        public XmlTagDefinition? LastTag { get; private set; }
        public string? LastPartialValue { get; private set; }

        public IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue, GameIndex index)
        {
            LastTag = tag;
            LastPartialValue = partialValue;
            return [];
        }
    }
}