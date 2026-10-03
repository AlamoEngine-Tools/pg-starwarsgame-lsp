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

    // ── TupleList: proposals follow the tag's slots ───────────────────────────
    //
    // The validation id used to pick the proposals - music events for context-name-pair, nothing
    // for context-name-list - so a terrain/model list completed nothing at all. The slots say what
    // each item is, and the item index runs past 1 because a list alternates for its whole length.

    private static readonly XmlTagDefinition MusicList = Tag("Music_Event_List_Ambient", XmlValueType.TupleList) with
    {
        Slots =
        [
            new TupleSlotDefinition { Label = "Context" },
            new TupleSlotDefinition
            {
                Label = "Music event", ReferenceKind = ReferenceKind.XmlObject,
                ObjectType = new GameObjectTypeDefinition { TypeName = "MusicEvent" }
            }
        ]
    };

    private static readonly XmlTagDefinition TerrainModel =
        Tag("Land_Terrain_Model_Mapping", XmlValueType.TupleList) with
        {
            Slots =
            [
                new TupleSlotDefinition
                {
                    Label = "Terrain", ReferenceKind = ReferenceKind.Enum,
                    Enum = new EnumDefinition
                    {
                        Name = "TerrainType", Kind = EnumKind.SchemaFixed,
                        Values = [new EnumValueDefinition { Name = "TEMPERATE" }]
                    }
                },
                new TupleSlotDefinition { Label = "Model", ReferenceKind = ReferenceKind.ModelFile }
            ]
        };

    [Fact]
    public void TupleList_AnUntypedSlot_ProposesNothing()
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);

        Assert.Empty(strategy.Handle(Ctx(MusicList, 0)));
        Assert.Null(completion.LastTag);
    }

    [Fact]
    public void TupleList_AnObjectSlot_ProposesThatObjectType()
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);

        strategy.Handle(Ctx(MusicList, 1)).ToList();

        Assert.Equal("MusicEvent", completion.LastTag?.ObjectType?.TypeName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void TupleList_AnEnumSlot_ProposesItsValues_AllAlongTheList(int itemIndex)
    {
        var proposals = new CapturingProposalRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), proposals, new CapturingCompletionRegistry());

        var result = strategy.Handle(Ctx(TerrainModel, itemIndex)).ToList();

        Assert.Equal("TerrainType", proposals.LastTag?.Enum?.Name);
        Assert.Contains(result, c => c.Label == "TEMPERATE");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void TupleList_AModelSlot_AsksForModelFiles(int itemIndex)
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);

        strategy.Handle(Ctx(TerrainModel, itemIndex)).ToList();

        Assert.Equal(ReferenceKind.ModelFile, completion.LastTag?.ReferenceKind);
    }

    // ── ListMap: the first item is a key; any later one may be a key or an item ─

    private static readonly XmlTagDefinition Buildables =
        Tag("Tactical_Buildable_Objects_Multiplayer", XmlValueType.ListMap) with
        {
            Slots =
            [
                new TupleSlotDefinition
                {
                    Label = "Faction", ReferenceKind = ReferenceKind.XmlObject,
                    ObjectType = new GameObjectTypeDefinition { TypeName = "Faction" }
                },
                new TupleSlotDefinition
                {
                    Label = "Object", ReferenceKind = ReferenceKind.XmlObject,
                    ObjectType = new GameObjectTypeDefinition { TypeName = "GameObjectType" }
                }
            ]
        };

    [Fact]
    public void ListMap_TheFirstItem_ProposesKeysOnly()
    {
        // The engine drops a value that does not start with a key, so nothing else belongs there.
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);

        strategy.Handle(Ctx(Buildables, 0)).ToList();

        Assert.Equal(["Faction"], completion.Asked.Select(t => t.ObjectType?.TypeName));
    }

    [Fact]
    public void ListMap_ALaterItem_ProposesKeysAndItems()
    {
        var completion = new CapturingCompletionRegistry();
        var strategy =
            new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(), completion);

        strategy.Handle(Ctx(Buildables, 3)).ToList();

        Assert.Equal(["Faction", "GameObjectType"], completion.Asked.Select(t => t.ObjectType?.TypeName));
    }

    [Fact]
    public void TupleList_WithoutSlots_ProposesNothing()
    {
        var strategy = new TupleValueCompletionStrategy(new FakeSchemaProvider(), new CapturingProposalRegistry(),
            new CapturingCompletionRegistry());

        Assert.Empty(strategy.Handle(Ctx(Tag("Music_Events", XmlValueType.TupleList), 1)));
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
        public List<XmlTagDefinition> Asked { get; } = [];

        public IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue, GameIndex index)
        {
            Asked.Add(tag);
            LastTag = tag;
            LastPartialValue = partialValue;
            return [];
        }
    }
}