// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Abilities;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     Reading <c>Unit_Abilities_Data</c>, which is a sub-object list rather than a scalar tag.
/// </summary>
/// <remarks>
///     Shared with the encyclopedia deliberately: it walked this same block with its own HAP pass,
///     and a second walker would be a second place for "which child names the type" to drift.
/// </remarks>
public sealed class UnitAbilityReaderTest
{
    private const string Abilities = """
        <Unit_Abilities_Data SubObjectList="Yes">
            <Unit_Ability>
                <Type>POWER_TO_WEAPONS</Type>
                <GUI_Activated_Ability_Name>Ev_Power_Ability</GUI_Activated_Ability_Name>
                <Recharge_Seconds>60</Recharge_Seconds>
            </Unit_Ability>
            <Unit_Ability>
                <Type>DEPLOY_TROOPERS</Type>
                <Owner_Attachment_Bone>B_Trooper_00</Owner_Attachment_Bone>
                <Particle_Effect>Home_One_Target_Particles</Particle_Effect>
            </Unit_Ability>
        </Unit_Abilities_Data>
        """;

    [Fact]
    public void Reader_ReturnsEveryAbilityInDocumentOrder()
    {
        var read = UnitAbilityReader.Read(Fragment(Abilities));

        Assert.Equal(["POWER_TO_WEAPONS", "DEPLOY_TROOPERS"], read.Select(a => a.Type));
    }

    [Fact]
    public void Reader_ExposesEveryChildTag_NotJustTheOnesOneCallerWanted()
    {
        // The encyclopedia wants the GUI name and the icon override; the preview wants the bone and
        // the particle. A reader that hard-coded either caller's fields would have to grow every
        // time the other one learns something.
        var read = UnitAbilityReader.Read(Fragment(Abilities));

        Assert.Equal("Ev_Power_Ability", read[0].Tag("GUI_Activated_Ability_Name"));
        Assert.Equal("60", read[0].Tag("Recharge_Seconds"));
        Assert.Equal("B_Trooper_00", read[1].Tag("Owner_Attachment_Bone"));
        Assert.Equal("Home_One_Target_Particles", read[1].Tag("Particle_Effect"));
    }

    [Fact]
    public void Reader_MatchesAChildTagHoweverItWasCased()
    {
        // HAP lower-cases element names, and the shipped files are not consistent anyway.
        Assert.Equal("B_Trooper_00", UnitAbilityReader.Read(Fragment(Abilities))[1]
            .Tag("owner_ATTACHMENT_bone"));
    }

    [Fact]
    public void Reader_SkipsAnAbilityWithNoType()
    {
        // Type is the identity. An entry without one cannot be bound to anything or drawn.
        var read = UnitAbilityReader.Read(Fragment("""
            <Unit_Abilities_Data>
                <Unit_Ability><Recharge_Seconds>5</Recharge_Seconds></Unit_Ability>
                <Unit_Ability><Type>TURBO</Type></Unit_Ability>
            </Unit_Abilities_Data>
            """));

        Assert.Equal(["TURBO"], read.Select(a => a.Type));
    }

    [Fact]
    public void Reader_IsEmptyWhenTheObjectDeclaresNoAbilities()
    {
        Assert.Empty(UnitAbilityReader.Read(null));
        Assert.Empty(UnitAbilityReader.Read("   "));
    }

    private static string Fragment(string xml)
    {
        return xml;
    }
}

/// <summary>
///     The abilities a subject carries, and which of its particle proxies each one drives.
/// </summary>
public sealed class PreviewAbilityTest
{
    [Fact]
    public void Abilities_ComeThroughWithTheirBoneAndParticle()
    {
        var scene = Scene(
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Unit_Abilities_Data", string.Empty, """
                <Unit_Abilities_Data>
                    <Unit_Ability>
                        <Type>DEPLOY_TROOPERS</Type>
                        <Owner_Attachment_Bone>B_Trooper_00</Owner_Attachment_Bone>
                        <Particle_Effect>Home_One_Target_Particles</Particle_Effect>
                        <Recharge_Seconds>45</Recharge_Seconds>
                    </Unit_Ability>
                </Unit_Abilities_Data>
                """));

        var ability = Assert.Single(scene.Abilities);
        Assert.Equal("DEPLOY_TROOPERS", ability.Type);
        Assert.Equal("B_Trooper_00", ability.OwnerAttachmentBone);
        Assert.Equal("Home_One_Target_Particles", ability.ParticleEffect);
        Assert.Equal(45f, ability.RechargeSeconds);
    }

    [Fact]
    public void Abilities_CarryTheirStatModifiers()
    {
        // The channel that makes a stat-only ability legible. DEFEND on the Nebulon B declares no
        // proxy, no bone, no particle and no clip - only these - so without them its row is a dead
        // switch with nothing to say. 316 uses over 9 kinds in foc.
        var scene = Scene(
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Unit_Abilities_Data", string.Empty, """
                <Unit_Abilities_Data>
                    <Unit_Ability>
                        <Type>DEFEND</Type>
                        <Mod_Multiplier>WEAPON_DELAY_MULTIPLIER, 3.0f</Mod_Multiplier>
                        <Mod_Multiplier>SPEED_MULTIPLIER, 0.8f</Mod_Multiplier>
                    </Unit_Ability>
                </Unit_Abilities_Data>
                """));

        var ability = Assert.Single(scene.Abilities);
        Assert.Equal(["WEAPON_DELAY_MULTIPLIER", "SPEED_MULTIPLIER"],
            ability.Modifiers.Select(m => m.Stat));
        Assert.Equal([3.0f, 0.8f], ability.Modifiers.Select(m => m.Factor));
    }

    [Fact]
    public void Abilities_ReadEveryModifier_NotJustTheFirst()
    {
        // `Mod_Multiplier` repeats inside one ability - Commander_Akbar_Team's DEFEND carries six -
        // and the reader that only kept one would describe the ability wrongly rather than partly.
        var scene = Scene(
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Unit_Abilities_Data", string.Empty, """
                <Unit_Abilities_Data>
                    <Unit_Ability>
                        <Type>DEFEND</Type>
                        <Mod_Multiplier>A, 1</Mod_Multiplier>
                        <Mod_Multiplier>B, 2</Mod_Multiplier>
                        <Mod_Multiplier>C, 3</Mod_Multiplier>
                    </Unit_Ability>
                </Unit_Abilities_Data>
                """));

        Assert.Equal(3, Assert.Single(scene.Abilities).Modifiers.Count);
    }

    [Fact]
    public void Abilities_DropAModifierThatIsNotANumber()
    {
        // The shipped values carry an `f` suffix - `3.0f` - which has to parse, but a row that is
        // not a number at all names no modifier.
        var scene = Scene(
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Unit_Abilities_Data", string.Empty, """
                <Unit_Abilities_Data>
                    <Unit_Ability>
                        <Type>DEFEND</Type>
                        <Mod_Multiplier>SPEED_MULTIPLIER, lots</Mod_Multiplier>
                        <Mod_Multiplier>SPEED_MULTIPLIER, 0.8f</Mod_Multiplier>
                    </Unit_Ability>
                </Unit_Abilities_Data>
                """));

        Assert.Equal([0.8f], Assert.Single(scene.Abilities).Modifiers.Select(m => m.Factor));
    }

    [Fact]
    public void Abilities_AreEmptyForASubjectThatDeclaresNone()
    {
        // Most objects do. 68 ability types exist and most drive nothing on the model.
        Assert.Empty(Scene(Tag("Space_Model_Name", "hull.alo")).Abilities);
    }

    private static PreviewScene Scene(params VariantTag[] shipTags)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = new[]
            {
                new GameSymbol("Ship", GameSymbolKind.XmlObject, "SpaceUnit",
                    new FileOrigin("file:///Ship.xml", 0, 0), null, null)
            }.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
                StringComparer.OrdinalIgnoreCase)
        };

        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            new FakeVariantTagSource().With("Ship", shipTags),
            new FakeAssets("hull.alo")).BuildForObject("Ship");
    }

    private static VariantTag Tag(string name, string value, string? fragment = null)
    {
        return new VariantTag(name, value, fragment ?? $"<{name}>{value}</{name}>", 0);
    }

}

/// <summary>
///     Binding a particle proxy to the ability that shows it, by its name prefix.
/// </summary>
/// <remarks>
///     39 such proxies in foc against 5404 ordinary <c>p_*</c> ones, so a lookup table is the right
///     size of solution. Measured in [[reference_unit_armament_and_ability_effects]].
/// </remarks>
public sealed class AbilityProxyPrefixTest
{
    [Theory]
    [InlineData("pptw_2mtank", "POWER_TO_WEAPONS")]
    [InlineData("PTE_Corvetteengines", "TURBO")]
    [InlineData("prs_at-aa_fx", "MISSILE_SHIELD")]
    [InlineData("pas_sprint", "SPRINT")]
    [InlineData("pem_invulnerability", "INVULNERABILITY")]
    [InlineData("pgw_grav_well", "INTERDICT")]
    public void EveryShippedPrefix_NamesItsAbility(string proxy, string type)
    {
        Assert.Equal(type, AbilityProxyPrefix.AbilityFor(proxy));
    }

    [Fact]
    public void APlainProxy_NamesNothing()
    {
        // 5404 of them. `p_atat_die` is not an ability proxy and must not be read as one.
        Assert.Null(AbilityProxyPrefix.AbilityFor("p_atat_die"));
        Assert.Null(AbilityProxyPrefix.AbilityFor(""));
    }

    [Fact]
    public void ThePrefixIsMatchedCaseInsensitively()
    {
        // `Ev_acclamator` writes `power_to_weapons` in lower case, and the proxies are as
        // inconsistent as the rest of the tree.
        Assert.Equal("TURBO", AbilityProxyPrefix.AbilityFor("pte_x"));
        Assert.Equal("TURBO", AbilityProxyPrefix.AbilityFor("PTE_X"));
    }

    [Fact]
    public void Bind_ListsAProxyUnderTheAbilityTheObjectActuallyDeclares()
    {
        // The shipped case: Rv_corvette carries PTE_Corvetteengines and the corvette declares TURBO.
        // Confirmed against a live server.
        var bound = AbilityProxyPrefix.Bind(["PTE_Corvetteengines"], ["TURBO"]);

        Assert.Equal(["PTE_Corvetteengines"], bound.For("TURBO"));
        Assert.Empty(bound.Unbound);
    }

    [Fact]
    public void Bind_LeavesAProxyUnboundWhenTheObjectDeclaresNoSuchAbility()
    {
        // `Nv_ipv1` carries `PTE_IPV1engine` while the object declares POWER_TO_WEAPONS and no
        // TURBO. The prefix is a HINT.
        var bound = AbilityProxyPrefix.Bind(["PTE_IPV1engine"], ["POWER_TO_WEAPONS"]);

        Assert.Empty(bound.For("POWER_TO_WEAPONS"));
        Assert.Equal([("TURBO", (IReadOnlyList<string>)["PTE_IPV1engine"])],
            bound.Unbound.Select(u => (u.Ability, u.ProxyNames)));
    }

    [Fact]
    public void Bind_ReportsAnUnboundProxyEvenWhenTHEOBJECTDECLARESNOABILITIES()
    {
        // `Ev_mdu_fieldgen` carries `prs_at-aa_fx` and declares nothing at all. Found by probing a
        // live server: an early return on "no abilities" skipped this scan entirely, and this is the
        // case where the effect is MOST likely to be a mistake worth seeing.
        var bound = AbilityProxyPrefix.Bind(["prs_at-aa_fx"], []);

        Assert.Single(bound.Unbound);
        Assert.Equal("MISSILE_SHIELD", bound.Unbound[0].Ability);
    }

    [Fact]
    public void Bind_IgnoresAnOrdinaryProxyEntirely()
    {
        // 5404 of them against 39 with a prefix. `p_atat_die` is neither bound nor reported.
        var bound = AbilityProxyPrefix.Bind(["p_atat_die", "p_engine_glow"], ["TURBO"]);

        Assert.Empty(bound.For("TURBO"));
        Assert.Empty(bound.Unbound);
    }

    [Fact]
    public void Bind_KeepsEveryProxyOfOneAbilityTogether()
    {
        // 20 PPTW_ proxies in foc, and one object may carry several.
        var bound = AbilityProxyPrefix.Bind(
            ["pptw_a", "pptw_b"], ["POWER_TO_WEAPONS"]);

        Assert.Equal(["pptw_a", "pptw_b"], bound.For("POWER_TO_WEAPONS"));
    }
}
