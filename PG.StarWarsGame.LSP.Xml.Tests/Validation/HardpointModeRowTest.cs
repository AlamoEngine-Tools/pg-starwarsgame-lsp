// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     A hardpoint's unit-mode rows against the abilities of the units attaching it (#140).
/// </summary>
/// <remarks>
///     Measured on the game: a hardpoint's <c>*_Mode_Weapon_Delay_Multiplier</c> and
///     <c>Fire_When_In_*_Mode</c> rows are read only for the owner's FIRST and SECOND unit ability,
///     keyed by that ability's type, and only while it is active; the rocket pair is read against a
///     ROCKET_ATTACK ability being active, in any slot. The slots are the ability list's document
///     order, and a variant's own list replaces the base's.
/// </remarks>
public sealed class HardpointModeRowTest
{
    private const string Uri = "file:///hardpoints.xml";

    // ── a mode row needs the ability in slot 0 or 1 ──────────────────────────

    [Fact]
    public void ModeRow_OwnerLacksTheAbility_IsReportedFromTheHardpointsFile()
    {
        const string text =
            """<X><HardPoint Name="HP_A"><Power_To_Weapons_Mode_Weapon_Delay_Multiplier>0.5</Power_To_Weapons_Mode_Weapon_Delay_Multiplier></HardPoint></X>""";
        var source = new HardpointTagSource()
            .With("SHIP", Hardpoints("HP_A"), Abilities("DEFEND"));

        var fact = Assert.Single(Produce(text, source, Sym("SHIP", "SpaceUnit"), Sym("HP_A", "HardPoint"))
            .OfType<HardpointModeRowWithoutAbilityFact>());

        Assert.Equal("HP_A", fact.HardpointId);
        Assert.Equal("SHIP", fact.OwnerId);
        Assert.Equal("Power_To_Weapons_Mode_Weapon_Delay_Multiplier", fact.TagName);
        Assert.Equal("POWER_TO_WEAPONS", fact.AbilityType);
        Assert.Equal(-1, fact.SlotOfType);
        Assert.Equal(0, fact.Line);
    }

    [Fact]
    public void ModeRow_AbilityInSlotZeroOrOne_IsFine()
    {
        const string text =
            """<X><HardPoint Name="HP_A"><Defend_Mode_Weapon_Delay_Multiplier>3</Defend_Mode_Weapon_Delay_Multiplier><Fire_When_In_Turbo_Mode>No</Fire_When_In_Turbo_Mode></HardPoint></X>""";
        var source = new HardpointTagSource()
            .With("SHIP", Hardpoints("HP_A"), Abilities("TURBO", "DEFEND"));

        Assert.Empty(Produce(text, source, Sym("SHIP", "SpaceUnit"), Sym("HP_A", "HardPoint"))
            .OfType<HardpointModeRowWithoutAbilityFact>());
    }

    [Fact]
    public void ModeRow_AbilityInSlotTwo_IsNeverConsulted()
    {
        // The game asks the hardpoint for slot 0's and slot 1's type only. Six shipped units carry
        // three abilities; none of their hardpoints carries a row for the third.
        const string text =
            """<X><HardPoint Name="HP_A"><Full_Salvo_Weapon_Delay_Multiplier>0.18</Full_Salvo_Weapon_Delay_Multiplier></HardPoint></X>""";
        var source = new HardpointTagSource()
            .With("SHIP", Hardpoints("HP_A"), Abilities("DEFEND", "TURBO", "FULL_SALVO"));

        var fact = Assert.Single(Produce(text, source, Sym("SHIP", "SpaceUnit"), Sym("HP_A", "HardPoint"))
            .OfType<HardpointModeRowWithoutAbilityFact>());

        Assert.Equal(2, fact.SlotOfType);
        Assert.Equal(["DEFEND", "TURBO", "FULL_SALVO"], fact.OwnerAbilities);
    }

    [Fact]
    public void ModeRow_ReportedFromTheOwnersFileToo_OnTheHardpointToken()
    {
        // Direction 2: the unit file is open; the squiggle lands on the hardpoint's name in the
        // HardPoints list, where the pairing is declared.
        const string text = """<X><SpaceUnit Name="SHIP"><HardPoints>HP_A</HardPoints></SpaceUnit></X>""";
        var source = new HardpointTagSource()
            .With("SHIP", Hardpoints("HP_A"), Abilities("DEFEND"))
            .With("HP_A", new VariantTag("Turbo_Mode_Weapon_Delay_Multiplier", "2", "", 0));

        var fact = Assert.Single(Produce(text, source, Sym("SHIP", "SpaceUnit"), Sym("HP_A", "HardPoint"))
            .OfType<HardpointModeRowWithoutAbilityFact>());

        Assert.Equal("TURBO", fact.AbilityType);
        Assert.Equal(4, fact.Length);
    }

    // ── the rocket pair ──────────────────────────────────────────────────────

    [Fact]
    public void RocketPair_NormalModeOff_WithoutRocketAttack_NeverFires()
    {
        // Fire_When_In_Normal_Attack_Mode = No fires only while ROCKET_ATTACK is active, and a unit
        // without the ability is never in that state: the hardpoint is a dead weapon.
        const string text =
            """<X><HardPoint Name="HP_A"><Fire_When_In_Rocket_Attack_Mode>Yes</Fire_When_In_Rocket_Attack_Mode><Fire_When_In_Normal_Attack_Mode>No</Fire_When_In_Normal_Attack_Mode></HardPoint></X>""";
        var source = new HardpointTagSource()
            .With("SHIP", Hardpoints("HP_A"), Abilities("DEFEND"));

        var facts = Produce(text, source, Sym("SHIP", "SpaceUnit"), Sym("HP_A", "HardPoint"));

        var dead = Assert.Single(facts.OfType<HardpointNeverFiresFact>());
        Assert.Equal("SHIP", dead.OwnerId);
        Assert.False(dead.BothFlagsOff);
        // The dead weapon is the finding; the inert rocket flag beside it is not reported twice.
        Assert.Empty(facts.OfType<HardpointModeRowWithoutAbilityFact>());
    }

    [Fact]
    public void RocketPair_BothOff_NeverFires_WhateverTheOwnerHas()
    {
        const string text =
            """<X><HardPoint Name="HP_A"><Fire_When_In_Rocket_Attack_Mode>No</Fire_When_In_Rocket_Attack_Mode><Fire_When_In_Normal_Attack_Mode>No</Fire_When_In_Normal_Attack_Mode></HardPoint></X>""";
        var source = new HardpointTagSource()
            .With("SHIP", Hardpoints("HP_A"), Abilities("ROCKET_ATTACK"));

        var dead = Assert.Single(Produce(text, source, Sym("SHIP", "SpaceUnit"), Sym("HP_A", "HardPoint"))
            .OfType<HardpointNeverFiresFact>());

        Assert.True(dead.BothFlagsOff);
    }

    [Fact]
    public void RocketPair_BothOff_IsReportedOnce_NotPerOwner()
    {
        // Both flags off kills the weapon whoever attaches it, so it is one finding about the
        // hardpoint - and it holds for a hardpoint nothing attaches as well.
        const string text =
            """<X><HardPoint Name="HP_A"><Fire_When_In_Rocket_Attack_Mode>No</Fire_When_In_Rocket_Attack_Mode><Fire_When_In_Normal_Attack_Mode>No</Fire_When_In_Normal_Attack_Mode></HardPoint><HardPoint Name="HP_B"><Fire_When_In_Rocket_Attack_Mode>No</Fire_When_In_Rocket_Attack_Mode><Fire_When_In_Normal_Attack_Mode>No</Fire_When_In_Normal_Attack_Mode></HardPoint></X>""";
        var source = new HardpointTagSource()
            .With("SHIP", Hardpoints("HP_A"), Abilities("DEFEND"))
            .With("OTHER", Hardpoints("HP_A"), Abilities("ROCKET_ATTACK"));

        var dead = Produce(text, source, Sym("SHIP", "SpaceUnit"), Sym("OTHER", "SpaceUnit"),
            Sym("HP_A", "HardPoint"), Sym("HP_B", "HardPoint")).OfType<HardpointNeverFiresFact>().ToList();

        Assert.Equal(["HP_A", "HP_B"], dead.Select(d => d.HardpointId).Order());
        Assert.All(dead, d => Assert.Null(d.OwnerId));
    }

    [Fact]
    public void RocketPair_OnARocketAttackUnit_InAnySlot_IsFine()
    {
        const string text =
            """<X><HardPoint Name="HP_A"><Fire_When_In_Rocket_Attack_Mode>Yes</Fire_When_In_Rocket_Attack_Mode><Fire_When_In_Normal_Attack_Mode>No</Fire_When_In_Normal_Attack_Mode></HardPoint></X>""";
        var source = new HardpointTagSource()
            .With("SHIP", Hardpoints("HP_A"), Abilities("DEFEND", "TURBO", "ROCKET_ATTACK"));

        var facts = Produce(text, source, Sym("SHIP", "SpaceUnit"), Sym("HP_A", "HardPoint"));

        Assert.Empty(facts.OfType<HardpointNeverFiresFact>());
        Assert.Empty(facts.OfType<HardpointModeRowWithoutAbilityFact>());
    }

    [Fact]
    public void RocketPair_RocketFlagAlone_WithoutRocketAttack_IsInert()
    {
        const string text =
            """<X><HardPoint Name="HP_A"><Fire_When_In_Rocket_Attack_Mode>No</Fire_When_In_Rocket_Attack_Mode></HardPoint></X>""";
        var source = new HardpointTagSource()
            .With("SHIP", Hardpoints("HP_A"), Abilities("DEFEND"));

        var facts = Produce(text, source, Sym("SHIP", "SpaceUnit"), Sym("HP_A", "HardPoint"));

        var inert = Assert.Single(facts.OfType<HardpointModeRowWithoutAbilityFact>());
        Assert.Equal("ROCKET_ATTACK", inert.AbilityType);
        Assert.Empty(facts.OfType<HardpointNeverFiresFact>());
    }

    [Fact]
    public void Variant_OwnListReplacesTheBases()
    {
        // Measured: a variant that declares Unit_Abilities_Data clears the base's list first, so
        // the base's DEFEND does not survive into the variant.
        const string text =
            """<X><HardPoint Name="HP_A"><Defend_Mode_Weapon_Delay_Multiplier>3</Defend_Mode_Weapon_Delay_Multiplier></HardPoint></X>""";
        var source = new HardpointTagSource()
            .With("BASE", Abilities("DEFEND"))
            .With("SHIP", new VariantTag("Variant_Of_Existing_Type", "BASE", "", 0), Hardpoints("HP_A"),
                Abilities("TURBO"));

        var fact = Assert.Single(Produce(text, source, Sym("BASE", "SpaceUnit"), Sym("SHIP", "SpaceUnit", "BASE"),
            Sym("HP_A", "HardPoint")).OfType<HardpointModeRowWithoutAbilityFact>());

        Assert.Equal("SHIP", fact.OwnerId);
        Assert.Equal(["TURBO"], fact.OwnerAbilities);
    }

    [Fact]
    public void Variant_WithoutOwnList_InheritsTheBases()
    {
        const string text =
            """<X><HardPoint Name="HP_A"><Defend_Mode_Weapon_Delay_Multiplier>3</Defend_Mode_Weapon_Delay_Multiplier></HardPoint></X>""";
        var source = new HardpointTagSource()
            .With("BASE", Abilities("DEFEND"))
            .With("SHIP", new VariantTag("Variant_Of_Existing_Type", "BASE", "", 0), Hardpoints("HP_A"));

        Assert.Empty(Produce(text, source, Sym("BASE", "SpaceUnit"), Sym("SHIP", "SpaceUnit", "BASE"),
            Sym("HP_A", "HardPoint")).OfType<HardpointModeRowWithoutAbilityFact>());
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static VariantTag Hardpoints(params string[] ids)
    {
        return new VariantTag("HardPoints", string.Join(", ", ids), "", 0);
    }

    private static VariantTag Abilities(params string[] types)
    {
        var entries = string.Concat(types.Select(t => $"<Unit_Ability><Type>{t}</Type></Unit_Ability>"));
        return new VariantTag("Unit_Abilities_Data", "", $"<Unit_Abilities_Data>{entries}</Unit_Abilities_Data>", 0);
    }

    private static IReadOnlyList<XmlFact> Produce(string text, IVariantTagSource source,
        params GameSymbol[] symbols)
    {
        var docSymbols = symbols.ToImmutableArray();
        var docIndex = new DocumentIndex(Uri, 1, docSymbols, ImmutableArray<GameReference>.Empty);
        var defs = symbols.ToImmutableDictionary(s => s.Id, ImmutableArray.Create,
            StringComparer.OrdinalIgnoreCase);
        // Every hardpoint is referenced from this document, so the attaching-object lookup finds the
        // owners wherever the fixture declares them.
        var references = symbols
            .Where(s => s.TypeName == "HardPoint")
            .ToImmutableDictionary(s => s.Id,
                s => ImmutableArray.Create(new GameReference(s.Id, GameSymbolKind.XmlObject, "HardPoint", Uri,
                    0, 0, s.Id.Length)),
                StringComparer.OrdinalIgnoreCase);
        var index = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty.Add(Uri, docIndex),
            WorkspaceDefinitions = defs,
            WorkspaceReferences = references
        };
        return new XmlHardpointFactProducer(new SchemaProviderBuilder().Build(), source)
            .Produce(Uri, ParsedXmlDocument.Parse(text), index);
    }

    private static GameSymbol Sym(string id, string typeName, string? variantOf = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin(Uri, 0, 0), null,
            variantOf);
    }

    private sealed class HardpointTagSource : IVariantTagSource
    {
        private readonly Dictionary<string, List<VariantTag>> _tags = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return _tags.GetValueOrDefault(objectId);
        }

        public HardpointTagSource With(string id, params VariantTag[] tags)
        {
            _tags[id] = [.. tags];
            return this;
        }
    }
}