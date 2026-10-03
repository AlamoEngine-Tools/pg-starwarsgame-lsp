// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Schema;

/// <summary>
///     Reading a repeating tuple value into its items: what each one says, where it sits, and which
///     slot it fills. Every consumer - validation, references, hover, completion - reads the value
///     this one way, so they cannot disagree about which item is the model.
/// </summary>
public sealed class TupleItemsTest
{
    private static readonly XmlTagDefinition TerrainModel = new()
    {
        Tag = "Land_Terrain_Model_Mapping",
        ValueType = XmlValueType.TupleList,
        Slots =
        [
            new TupleSlotDefinition { Label = "Terrain", ReferenceKind = ReferenceKind.Enum },
            new TupleSlotDefinition { Label = "Model", ReferenceKind = ReferenceKind.ModelFile }
        ]
    };

    [Fact]
    public void Read_AlternatesTheSlots()
    {
        var items = TupleItems.Read(TerrainModel, "Temperate, EI_TROOPER.ALO, Arctic, EI_TROOPER_Snow.ALO");

        Assert.Equal(["Temperate", "EI_TROOPER.ALO", "Arctic", "EI_TROOPER_Snow.ALO"], items.Select(i => i.Text));
        Assert.Equal([0, 1, 0, 1], items.Select(i => i.SlotIndex));
        Assert.Equal(["Terrain", "Model", "Terrain", "Model"], items.Select(i => i.Slot!.Label));
    }

    [Fact]
    public void Read_GivesEachItemItsOffsetInTheRawValue()
    {
        const string raw = "Temperate,  EI_TROOPER.ALO";

        var items = TupleItems.Read(TerrainModel, raw);

        Assert.Equal(0, items[0].Offset);
        Assert.Equal(raw.IndexOf("EI_TROOPER", StringComparison.Ordinal), items[1].Offset);
    }

    [Fact]
    public void Read_SpansLines_AndIgnoresATrailingComma()
    {
        // The shipped shape: one pair per line, and the list ends on a comma.
        const string raw = "\n    Temperate, EI_TROOPER.ALO,\n    Urban, EI_TROOPER.ALO,\n";

        var items = TupleItems.Read(TerrainModel, raw);

        Assert.Equal(4, items.Count);
        Assert.Equal("Urban", items[2].Text);
        Assert.Equal(raw.IndexOf("Urban", StringComparison.Ordinal), items[2].Offset);
    }

    [Fact]
    public void Read_ATagWithoutSlots_StillReadsItems_WithNoSlot()
    {
        var untyped = TerrainModel with { Slots = [] };

        var items = TupleItems.Read(untyped, "a, b");

        Assert.Equal(["a", "b"], items.Select(i => i.Text));
        Assert.All(items, i => Assert.Null(i.Slot));
    }

    [Fact]
    public void GroupSize_IsTheSlotCount_OrAPairWithoutSlots()
    {
        Assert.Equal(2, TupleItems.GroupSize(TerrainModel));
        Assert.Equal(2, TupleItems.GroupSize(TerrainModel with { Slots = [] }));
    }

    [Fact]
    public void Describe_NamesTheSlots()
    {
        Assert.Equal("`Terrain, Model` pairs", TupleItems.Describe(TerrainModel));
        Assert.Equal("comma-separated pairs", TupleItems.Describe(TerrainModel with { Slots = [] }));
    }

    // ── ListMap: an item is a key when it IS one, wherever it sits ─────────────
    //
    // Measured in the engine: every item is tried as a key first and only then added to the current
    // key's list. So "Empire, A, B, Rebel, C" is two groups, and an object that happens to be named
    // like a key starts a group of its own.

    private static readonly XmlTagDefinition Buildables = new()
    {
        Tag = "Tactical_Buildable_Objects_Multiplayer",
        ValueType = XmlValueType.ListMap,
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

    private static readonly XmlTagDefinition Presence = new()
    {
        Tag = "Presence_Induced_Animations",
        ValueType = XmlValueType.ListMap,
        Slots =
        [
            new TupleSlotDefinition
            {
                Label = "Animation", ReferenceKind = ReferenceKind.Enum,
                Enum = new EnumDefinition
                {
                    Name = "AnimationType", Kind = EnumKind.SchemaFixed,
                    Values =
                    [
                        new EnumValueDefinition { Name = "Attention" }, new EnumValueDefinition { Name = "Idle" },
                        new EnumValueDefinition { Name = "Turn Left" }
                    ]
                }
            },
            new TupleSlotDefinition
            {
                Label = "Unit", ReferenceKind = ReferenceKind.XmlObject,
                ObjectType = new GameObjectTypeDefinition { TypeName = "GameObjectType" }
            }
        ]
    };

    private static GameIndex IndexOf(params (string Id, string Type)[] symbols)
    {
        var map = ImmutableDictionary.Create<string, ImmutableArray<GameSymbol>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, type) in symbols)
            map = map.Add(id, [new GameSymbol(id, GameSymbolKind.XmlObject, type, new UnknownOrigin("test"), null)]);
        return GameIndex.Empty with { WorkspaceDefinitions = map };
    }

    [Fact]
    public void Read_ListMap_StartsAGroupAtEveryKey()
    {
        var index = IndexOf(("Empire", "Faction"), ("Rebel", "Faction"), ("A", "GameObjectType"));

        var items = TupleItems.Read(Buildables, "Empire, A, B, Rebel, C", ListMapKeys.FromIndex(Buildables, index));

        Assert.Equal([0, 1, 1, 0, 1], items.Select(i => i.SlotIndex));
        Assert.Equal(["Faction", "Object", "Object", "Faction", "Object"], items.Select(i => i.Slot!.Label));
    }

    [Fact]
    public void Read_ListMap_AnItemNamedLikeAKey_StartsAGroup()
    {
        // The engine's own collision: "Idle" is an animation state, so it opens a group even where
        // the author meant a unit called Idle.
        var items = TupleItems.Read(Presence, "Attention, Idle, Darth_Vader");

        Assert.Equal([0, 0, 1], items.Select(i => i.SlotIndex));
    }

    [Fact]
    public void Read_ListMap_AnEnumKey_IsJudgedFromTheSchemaAlone_SpacesAndCaseAsTheEngineDoes()
    {
        var items = TupleItems.Read(Presence, " turn left , Darth_Vader");

        Assert.Equal([0, 1], items.Select(i => i.SlotIndex));
    }

    [Fact]
    public void Read_ListMap_AnObjectKeyWithoutAnIndex_FallsBackToPosition()
    {
        // Nothing to look a faction up in - the parser's view. First item the key, the rest items.
        var items = TupleItems.Read(Buildables, "Empire, A, Rebel");

        Assert.Equal([0, 1, 1], items.Select(i => i.SlotIndex));
    }

    [Fact]
    public void ListMapKeys_AnObjectKey_MustBeOfTheKeyType()
    {
        // The engine looks keys up in a table of factions alone, so an object of another type with
        // the right name is no key.
        var index = IndexOf(("Empire", "Faction"), ("Rebel", "GameObjectType"));
        var isKey = ListMapKeys.FromIndex(Buildables, index);

        Assert.True(isKey("empire"));
        Assert.False(isKey("Rebel"));
        Assert.False(isKey("Nobody"));
    }

    [Fact]
    public void ListMapKeys_FromSchema_IsNull_ForAnObjectKey()
    {
        Assert.Null(ListMapKeys.FromSchema(Buildables));
        Assert.NotNull(ListMapKeys.FromSchema(Presence));
    }

    [Fact]
    public void Describe_AListMap_SaysKeyThenItems()
    {
        Assert.Equal("`Animation` keys, each followed by `Unit` items", TupleItems.Describe(Presence));
    }

    [Fact]
    public void Describe_AGroupOfThree_IsNotCalledAPair()
    {
        var three = TerrainModel with
        {
            Slots = [.. TerrainModel.Slots, new TupleSlotDefinition { Label = "Weight" }]
        };

        Assert.Equal("`Terrain, Model, Weight` groups", TupleItems.Describe(three));
    }
}