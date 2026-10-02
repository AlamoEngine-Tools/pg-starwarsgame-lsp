// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Providers;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     What each item of a repeating tuple value is. <c>TupleList</c> is the engine's generic pair
///     list, and without slots every consumer guessed - and guessed "music events" for all three
///     tags that use it, one of which is a list of terrain/model pairs.
/// </summary>
public sealed class EawSchemaTupleSlotTest
{
    private static readonly Lazy<LocalFileSchemaProvider> Shipped = new(() =>
        new LocalFileSchemaProvider(EawSchemaRepo.Root, new FileSystem(),
            NullLogger<LocalFileSchemaProvider>.Instance));

    [Fact]
    public void Parser_ReadsEachSlotInOrder()
    {
        const string yaml = """
                            tags:
                              - tag: Pairs
                                type: TupleList
                                slots:
                                  - label: Terrain
                                    referenceKind: enum
                                    enumName: TerrainType
                                  - label: Model
                                    referenceKind: modelFile
                                  - label: Note
                            """;

        var slots = Assert.Single(YamlSchemaParser.ParseTagFile(yaml)).Slots;

        Assert.Equal(["Terrain", "Model", "Note"], slots.Select(s => s.Label));
        Assert.Equal([ReferenceKind.Enum, ReferenceKind.ModelFile, ReferenceKind.None],
            slots.Select(s => s.ReferenceKind));
        Assert.Equal("TerrainType", slots[0].EnumName);
    }

    [Fact]
    public void Parser_AnUnknownSlotKind_LeavesTheSlotUntyped()
    {
        // Lenient like every other referenceKind here: an untyped slot checks nothing, which is
        // the safe failure - a guessed type would report problems that are not there.
        const string yaml = """
                            tags:
                              - tag: Pairs
                                type: TupleList
                                slots:
                                  - label: Thing
                                    referenceKind: somethingNew
                            """;

        Assert.Equal(ReferenceKind.None, Assert.Single(YamlSchemaParser.ParseTagFile(yaml)).Slots[0].ReferenceKind);
    }

    [Fact]
    public void Parser_ATagWithoutSlots_HasNone()
    {
        const string yaml = """
                            tags:
                              - tag: Max_Speed
                                type: Float
                            """;

        Assert.Empty(Assert.Single(YamlSchemaParser.ParseTagFile(yaml)).Slots);
    }

    [Fact]
    public void Shipped_TerrainModelMapping_IsATerrainThenAModel()
    {
        var slots = Shipped.Value.GetTag("Land_Terrain_Model_Mapping")!.Slots;

        Assert.Equal(["Terrain", "Model"], slots.Select(s => s.Label));
        Assert.Equal(ReferenceKind.Enum, slots[0].ReferenceKind);
        Assert.Equal("TerrainType", slots[0].Enum?.Name);
        Assert.Equal(ReferenceKind.ModelFile, slots[1].ReferenceKind);
    }

    [Theory]
    [InlineData("Music_Event_List_Ambient")]
    [InlineData("Music_Event_List_Battle")]
    public void Shipped_MusicLists_AreAnUntypedContextThenAMusicEvent(string tag)
    {
        // The context is the seven terrains plus Space in the shipped data, so it is NOT
        // TerrainType - typing it so would flag every Space.
        var definition = Shipped.Value.GetTagsForType("Faction")
            .Single(t => t.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(["Context", "Music event"], definition.Slots.Select(s => s.Label));
        Assert.Equal(ReferenceKind.None, definition.Slots[0].ReferenceKind);
        Assert.Equal(ReferenceKind.XmlObject, definition.Slots[1].ReferenceKind);
        Assert.Equal("MusicEvent", definition.Slots[1].ObjectType?.TypeName);
    }

    // ── ListMap: a key, then the items it maps to ──────────────────────────────

    [Fact]
    public void Shipped_PresenceInducedAnimations_MapsAnAnimationStateToUnits()
    {
        var tag = Shipped.Value.GetTag("Presence_Induced_Animations")!;

        Assert.Equal(XmlValueType.ListMap, tag.ValueType);
        Assert.Equal(["Animation", "Unit"], tag.Slots.Select(s => s.Label));
        Assert.Equal("AnimationType", tag.Slots[0].Enum?.Name);
        Assert.Equal("GameObjectType", tag.Slots[1].ObjectType?.TypeName);
        // The engine appends a repeated tag outside a variant, so a second one is not dead text.
        Assert.True(tag.MultipleAllowed);
        // The slots say everything the named handler used to; a replace override would also drop
        // the default checks the slots now feed.
        Assert.Null(tag.ValidationOverride);
    }

    [Theory]
    [InlineData("Tactical_Buildable_Objects_Campaign")]
    [InlineData("Tactical_Buildable_Objects_Multiplayer")]
    public void Shipped_TacticalBuildables_MapAFactionToObjects(string name)
    {
        var tag = Shipped.Value.GetTag(name)!;

        Assert.Equal(XmlValueType.ListMap, tag.ValueType);
        Assert.Equal(["Faction", "Object"], tag.Slots.Select(s => s.Label));
        Assert.Equal("Faction", tag.Slots[0].ObjectType?.TypeName);
        Assert.Equal("GameObjectType", tag.Slots[1].ObjectType?.TypeName);
        Assert.True(tag.MultipleAllowed);
    }

    [Fact]
    public void Shipped_EveryListMapTag_DeclaresAKeyAndAValueSlot()
    {
        var wrong = Shipped.Value.AllTags
            .Where(t => t.ValueType == XmlValueType.ListMap && t.Slots.Count != 2)
            .Select(t => $"{t.Tag} ({t.Slots.Count})")
            .ToList();

        Assert.True(wrong.Count == 0, "ListMap tags without exactly two slots: " + string.Join(", ", wrong));
    }

    [Fact]
    public void Shipped_AiPlayerControl_IsTheEngineTypeItIsReadAs()
    {
        // The engine's type table maps it to 27, a name list - never to the key-to-list type it
        // was filed under, whose reader would have split a faction pair into keys and items.
        var tag = Shipped.Value.GetTagsForType("Campaign")
            .Single(t => t.Tag.Equals("AI_Player_Control", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(XmlValueType.NameReferenceList, tag.ValueType);
    }

    [Fact]
    public void Shipped_AnimationType_IsTheEngineList_SpacesAndSpellingIncluded()
    {
        var values = Shipped.Value.GetEnum("AnimationType")!.Values.Select(v => v.Name).ToList();

        Assert.Equal(117, values.Count);
        Assert.Contains("Turn Left", values);
        Assert.Contains("Transition From Move 120", values);
        Assert.Contains("Force_Revel_Begin", values);
        Assert.Contains("Attention", values);
        Assert.Contains("Celebrate", values);
        Assert.Equal(values.Count, values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Shipped_EveryTupleListTag_DeclaresItsSlots()
    {
        // The type says nothing about what the items are, so a TupleList tag without slots is one
        // nothing can check, hover or complete.
        var missing = Shipped.Value.AllTags
            .Where(t => t.ValueType == XmlValueType.TupleList && t.Slots.Count == 0)
            .Select(t => t.Tag)
            .ToList();

        Assert.True(missing.Count == 0, "TupleList tags without slots: " + string.Join(", ", missing));
    }
}