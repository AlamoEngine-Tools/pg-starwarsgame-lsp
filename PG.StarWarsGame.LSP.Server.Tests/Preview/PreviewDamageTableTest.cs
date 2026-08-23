// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The damage-versus-armor table, and what a hit on this subject has to get through.
/// </summary>
/// <remarks>
///     <para>
///         <c>Damage_To_Armor_Mod</c> is 2426 rows in foc and 1654 in eaw, over 81 damage types and
///         53 armor types - so 4293 possible pairs and a table that names barely half of them. The
///         gap is not an authoring mistake: a missing pair is 1.0.
///     </para>
///     <para>
///         Read through <see cref="RepeatedTagReader" />, like the reticle rows: resolving
///         GameConstants through the effective-object resolver collapses a repeated tag to its last
///         occurrence and would yield exactly ONE of the 2426.
///     </para>
/// </remarks>
public sealed class PreviewDamageTableTest
{
    private static PreviewDamageTable Build()
    {
        var tags = new FakeVariantTagSource().With("GameConstants",
            Tag("Damage_To_Armor_Mod", " Damage_Default, Armor_Default, 1 "),
            Tag("Damage_To_Armor_Mod", " Damage_Default, Shield_Capital, 0.5 "),
            Tag("Damage_To_Armor_Mod", " Damage_Anti_Fighter, Armor_Star_Destroyer, 0.25 "),
            Tag("Damage_To_Armor_Mod", " Damage_Anti_Fighter, Shield_Capital, 2 "),
            Tag("Damage_To_Armor_Mod", " Damage_Ion, Armor_Star_Destroyer, 4 "));

        return PreviewDamageTable.From(GameIndex.Empty, tags);
    }

    [Fact]
    public void Table_ReadsEveryRow_NotJustTheLastOccurrence()
    {
        // The whole reason RepeatedTagReader exists. If this returns 1 the resolver has been used.
        Assert.Equal(3, Build().DamageTypes.Count);
    }

    [Fact]
    public void Table_ListsEveryDamageTypeItSaw_ForThePicker()
    {
        // The attacker panel offers all 81 the tree declares - including a type with no row against
        // this target's armor, which is a perfectly ordinary choice that resolves to 1.0.
        Assert.Equal(["Damage_Anti_Fighter", "Damage_Default", "Damage_Ion"],
            Build().DamageTypes);
    }

    [Fact]
    public void Table_ListsADamageTypeTheMatrixNeverMentions()
    {
        // `Damage_Types` is the declared list; `Damage_To_Armor_Mod` is only where factors are set.
        // The shipped file's own comment marks the tail of the list - Damage_Normal among them - as
        // coming "from hard-coded damage enumeration", and none of that tail has a matrix row. So
        // reading the picker off the matrix dropped exactly the type the Star Destroyer names in
        // its `Death_Clone`, and its wreck could never be produced.
        var tags = new FakeVariantTagSource().With("GameConstants",
            Tag("Damage_Types", " Damage_Ion, Damage_Normal "),
            Tag("Damage_To_Armor_Mod", " Damage_Ion, Armor_Star_Destroyer, 4 "));

        var table = PreviewDamageTable.From(GameIndex.Empty, tags);

        Assert.Equal(["Damage_Ion", "Damage_Normal"], table.DamageTypes);
    }

    [Fact]
    public void Table_UnionsTheDeclaredListWithTheMatrix()
    {
        // A type with a factor but no place in the list is still offered: the list is the engine's
        // enumeration, and a mod that adds a row without touching it has still added a damage type.
        var tags = new FakeVariantTagSource().With("GameConstants",
            Tag("Damage_Types", " Damage_Normal "),
            Tag("Damage_To_Armor_Mod", " Damage_Ion, Armor_Star_Destroyer, 4 "));

        Assert.Equal(["Damage_Ion", "Damage_Normal"],
            PreviewDamageTable.From(GameIndex.Empty, tags).DamageTypes);
    }

    [Fact]
    public void Factors_AreKeyedByDamageTypeForOneArmor()
    {
        var factors = Build().FactorsFor("Armor_Star_Destroyer");

        Assert.Equal(0.25f, factors["Damage_Anti_Fighter"]);
        Assert.Equal(4f, factors["Damage_Ion"]);

        // Only the pairs that exist. Damage_Default has no row against this armor, and inventing a
        // 1.0 entry here would make the wire claim the table says something it does not.
        Assert.False(factors.ContainsKey("Damage_Default"));
    }

    [Fact]
    public void Factors_AreMatchedCaseInsensitively()
    {
        // The shipped file is not consistent about casing, here or in the reticle rows.
        Assert.Equal(4f, Build().FactorsFor("armor_star_destroyer")["Damage_Ion"]);
    }

    [Fact]
    public void Factors_ForAnArmorNothingMentions_AreEmpty()
    {
        // Every pair missing means every pair is 1.0, which is a real and common state.
        Assert.Empty(Build().FactorsFor("Armor_Invented"));
    }

    [Fact]
    public void Factors_ForNoArmorAtAll_AreEmpty()
    {
        // An object that declares no Armor_Type. 72 objects in foc declare one, so most do not.
        Assert.Empty(Build().FactorsFor(null));
    }

    [Fact]
    public void Table_DropsAMalformedRow()
    {
        // Two fields instead of three, or a factor that is not a number. Padding it would invent a
        // multiplier nobody wrote.
        var tags = new FakeVariantTagSource().With("GameConstants",
            Tag("Damage_To_Armor_Mod", " Damage_Default, Armor_Default "),
            Tag("Damage_To_Armor_Mod", " Damage_Default, Armor_Bomber, heavy "),
            Tag("Damage_To_Armor_Mod", " Damage_Ion, Armor_Bomber, 3 "));

        var table = PreviewDamageTable.From(GameIndex.Empty, tags);

        Assert.Equal(["Damage_Ion"], table.DamageTypes);
        Assert.Equal(3f, table.FactorsFor("Armor_Bomber")["Damage_Ion"]);
    }

    [Fact]
    public void Table_IsEmptyWhenGameConstantsIsAbsent()
    {
        var table = PreviewDamageTable.From(GameIndex.Empty, new FakeVariantTagSource());

        Assert.Empty(table.DamageTypes);
        Assert.Empty(table.FactorsFor("Armor_Default"));
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }
}

/// <summary>
///     What the scene reports about the subject as a TARGET - the object on stage is the thing being
///     shot at, and D1's panel is a weapon the reader builds to shoot it with.
/// </summary>
public sealed class PreviewTargetDefenceTest
{
    [Fact]
    public void Defence_ReadsThePoolsAndArmorsOffTheTarget()
    {
        var scene = Scene(
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Armor_Type", " Armor_Star_Destroyer "),
            Tag("Shield_Armor_Type", "Shield_Capital"),
            Tag("Shield_Points", "2000"),
            Tag("Tactical_Health", "7500"),
            Tag("Energy_Capacity", "8000"),
            Tag("SpaceBehavior", "POWERED, SHIELDED, TARGETING"));

        var defence = scene.Defence;
        Assert.NotNull(defence);
        Assert.Equal("Armor_Star_Destroyer", defence!.ArmorType);
        Assert.Equal("Shield_Capital", defence.ShieldArmorType);
        Assert.Equal(2000f, defence.ShieldPoints);
        Assert.Equal(7500f, defence.TacticalHealth);
        Assert.Equal(8000f, defence.EnergyCapacity);
    }

    [Theory]
    [InlineData("SpaceBehavior")]
    [InlineData("LandBehavior")]
    [InlineData("Behavior")]
    public void Defence_FindsShieldedInAnyOfTheThreeBehaviourLists(string tag)
    {
        // MEASURED over foc: SHIELDED appears in SpaceBehavior 98 times, in plain Behavior 32 and in
        // LandBehavior 19. Checking only the two the weapon code checks would call a third of the
        // shielded objects unshielded.
        var scene = Scene(Tag("Space_Model_Name", "hull.alo"), Tag(tag, "POWERED, SHIELDED"));

        Assert.True(scene.Defence!.IsShielded);
    }

    [Fact]
    public void Defence_SaysUnshieldedWhenNoListNamesIt()
    {
        var scene = Scene(Tag("Space_Model_Name", "hull.alo"),
            Tag("SpaceBehavior", "POWERED, TARGETING"), Tag("Shield_Points", "2000"));

        // Shield_Points alone does not make a shield: SHIELDED is what puts one in play, and an
        // object carrying leftover points without the behaviour would otherwise be unkillable.
        Assert.False(scene.Defence!.IsShielded);
    }

    [Fact]
    public void Defence_MatchesTheBehaviourTokenWhole()
    {
        // `UNSHIELDED` and `SHIELDED_FIGHTER` must not read as SHIELDED. A substring test on a
        // comma list is how that goes wrong.
        var scene = Scene(Tag("Space_Model_Name", "hull.alo"), Tag("SpaceBehavior", "UNSHIELDED"));

        Assert.False(scene.Defence!.IsShielded);
    }

    [Fact]
    public void Defence_CarriesOnlyTheFactorsForThisTargetsTwoArmors()
    {
        // Not the whole 2426-row table. The armor axis is fixed by the target - one Armor_Type and
        // one Shield_Armor_Type - so at most 81 rows per axis reach the wire instead of all of them.
        var scene = Scene(
            [Tag("Damage_To_Armor_Mod", " Damage_Ion, Armor_Star_Destroyer, 4 "),
                Tag("Damage_To_Armor_Mod", " Damage_Ion, Shield_Capital, 0.5 "),
                Tag("Damage_To_Armor_Mod", " Damage_Ion, Armor_Bomber, 9 ")],
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Armor_Type", "Armor_Star_Destroyer"),
            Tag("Shield_Armor_Type", "Shield_Capital"));

        Assert.Equal(4f, scene.Defence!.HullFactors["Damage_Ion"]);
        Assert.Equal(0.5f, scene.Defence.ShieldFactors["Damage_Ion"]);
        Assert.DoesNotContain("Armor_Bomber", string.Join(",", scene.Defence.HullFactors.Keys));
    }

    [Fact]
    public void Defence_OffersEveryDamageTypeEvenWithNoRowAgainstThisArmor()
    {
        // The picker holds all of them. A type with no pair against this target is not an invalid
        // choice - it is a 1.0, which is exactly what a modder needs to be able to see.
        var scene = Scene(
            [Tag("Damage_To_Armor_Mod", " Damage_Ion, Armor_Bomber, 9 "),
                Tag("Damage_To_Armor_Mod", " Damage_Anti_Fighter, Armor_Bomber, 2 ")],
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Armor_Type", "Armor_Star_Destroyer"));

        Assert.Equal(["Damage_Anti_Fighter", "Damage_Ion"], scene.Defence!.DamageTypes);
        Assert.Empty(scene.Defence.HullFactors);
    }

    [Fact]
    public void Defence_TotalsTheHealthOfEveryDestructibleHardpoint()
    {
        // The convention most mods author to: a unit with hardpoints has its health set to the SUM
        // of theirs. Nobody knows what the engine's own
        // Hull_Vs_Hard_Points_Health_Constraint (0.2) computes - the community never worked it out -
        // so the preview carries BOTH numbers and lets the reader see them. The Star Destroyer
        // declares Tactical_Health 2000 against 4075 of hardpoint health, so they can differ wildly.
        var scene = Ship(
            [Tag("Space_Model_Name", "hull.alo"), Tag("Tactical_Health", "2000"),
                Tag("HardPoints", "HP_A, HP_B, HP_C")],
            ("HP_A", [Tag("Health", "350"), Tag("Is_Destroyable", "Yes")]),
            ("HP_B", [Tag("Health", "375"), Tag("Is_Destroyable", "Yes")]),
            ("HP_C", [Tag("Health", "325"), Tag("Is_Destroyable", "Yes")]));

        Assert.Equal(1050f, scene.Defence!.HardpointHealthTotal);
        Assert.Equal(2000f, scene.Defence.TacticalHealth);
    }

    [Fact]
    public void Defence_CountsAnUntargetableHardpointTowardsTheTotal()
    {
        // Untargetable but destructible is a supported shape, not an authoring mistake: the game
        // warns about it and at least two mods use it as a gameplay element. It cannot be shot at
        // directly, but it still has to die before the unit does - so its health is part of the pool.
        var scene = Ship(
            [Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_A, HP_Hidden")],
            ("HP_A", [Tag("Health", "350"), Tag("Is_Destroyable", "Yes"), Tag("Is_Targetable", "Yes")]),
            ("HP_Hidden", [Tag("Health", "100"), Tag("Is_Destroyable", "Yes"), Tag("Is_Targetable", "No")]));

        Assert.Equal(450f, scene.Defence!.HardpointHealthTotal);
    }

    [Fact]
    public void Defence_LeavesAnIndestructibleHardpointOutOfTheTotal()
    {
        // It can never die, so it can never contribute to the unit dying either.
        var scene = Ship(
            [Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_A, HP_Fixed")],
            ("HP_A", [Tag("Health", "350"), Tag("Is_Destroyable", "Yes")]),
            ("HP_Fixed", [Tag("Health", "999"), Tag("Is_Destroyable", "No")]));

        Assert.Equal(350f, scene.Defence!.HardpointHealthTotal);
    }

    [Fact]
    public void Defence_HasNoHardpointTotalForAUnitWithoutHardpoints()
    {
        // A fighter takes damage on its own pools directly, so there is no second number to show.
        var scene = Scene(Tag("Space_Model_Name", "hull.alo"), Tag("Tactical_Health", "400"));

        Assert.Null(scene.Defence!.HardpointHealthTotal);
    }

    [Fact]
    public void Defence_IsAbsentForABareModel()
    {
        // A `.alo` opened directly has no XML behind it, so there is nothing to be shot at.
        var scene = new PreviewSceneBuilder(new FakeGameIndexService(GameIndex.Empty),
                new NullSchemaProvider(), new FakeVariantTagSource(), new FakeAssets("hull.alo"))
            .BuildForModel("hull.alo");

        Assert.Null(scene.Defence);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static PreviewScene Scene(params VariantTag[] shipTags)
    {
        return Scene([], shipTags);
    }

    private static PreviewScene Scene(VariantTag[] constants, params VariantTag[] shipTags)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = new[] { Sym("Ship", "SpaceUnit") }.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };

        var tags = new FakeVariantTagSource().With("Ship", shipTags);
        if (constants.Length > 0)
            tags = tags.With("GameConstants", constants);

        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, new FakeAssets("hull.alo")).BuildForObject("Ship");
    }

    private static PreviewScene Ship(
        VariantTag[] shipTags, params (string Id, VariantTag[] Tags)[] hardpoints)
    {
        var symbols = new List<GameSymbol> { Sym("Ship", "SpaceUnit") };
        symbols.AddRange(hardpoints.Select(h => Sym(h.Id, "HardPoint")));

        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };

        var tags = new FakeVariantTagSource().With("Ship", shipTags);
        foreach (var hardpoint in hardpoints)
            tags = tags.With(hardpoint.Id, hardpoint.Tags);

        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, new FakeAssets("hull.alo")).BuildForObject("Ship");
    }

    private static GameSymbol Sym(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin($"file:///{id}.xml", 0, 0), null, null);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }
}
