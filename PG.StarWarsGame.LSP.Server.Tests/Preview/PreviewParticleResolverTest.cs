// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     Working out which particle system hangs off which bone, and what switches it on.
/// </summary>
/// <remarks>
///     Measured against <c>Ev_stardestroyer.alo</c>, which carries 22 proxies: twenty
///     <c>p_hp_imperial_damage</c> whose bones' parents are the <c>HP_*_EmitDamage</c> bones named by
///     each hardpoint's <c>Damage_Particles</c>, one <c>pe_stardestroyerengines</c> under
///     <c>engines_big</c>, and one <c>pi_damage_elec_SD00</c> the file itself marks invisible.
/// </remarks>
public sealed class PreviewParticleResolverTest
{
    private static AlamoModelBone Bone(int index, string name, int parent = -1)
    {
        return new AlamoModelBone(index, name, parent, true, AlamoBillboardType.Disable,
            Matrix4x4.Identity, Matrix4x4.Identity);
    }

    private static AlamoProxy Proxy(string name, int boneIndex, bool visible = true,
        bool altDecreaseStayHidden = false, int? alt = null, int? lod = null)
    {
        return new AlamoProxy(name, boneIndex, visible, altDecreaseStayHidden, alt, lod);
    }

    private static PreviewHardpoint Hardpoint(
        string id, string? damageBone = null, string? engineBone = null,
        bool engineDeathHides = false)
    {
        return new PreviewHardpoint(id, null, null, null, true, true, null, damageBone, null, null,
            engineBone, null, null, engineDeathHides, null, null);
    }

    /// <summary>The Star Destroyer's shape: a damage bone with two smoke proxies hanging under it.</summary>
    private static (List<AlamoModelBone> Bones, List<AlamoProxy> Proxies) Destroyer()
    {
        var bones = new List<AlamoModelBone>
        {
            Bone(0, "EV_StarDestroyer"),
            Bone(1, "HP_F-L_EmitDamage", 0),
            Bone(2, "p_hp_imperial_damage", 1),
            Bone(3, "p_hp_imperial_damage", 1),
            Bone(4, "engines_big", 0),
            Bone(5, "pe_stardestroyerengines", 4),
            Bone(6, "pi_damage_elec_SD00", 0)
        };

        var proxies = new List<AlamoProxy>
        {
            Proxy("p_hp_imperial_damage", 2),
            Proxy("p_hp_imperial_damage", 3),
            Proxy("pe_stardestroyerengines", 5),
            Proxy("pi_damage_elec_SD00", 6, false)
        };

        return (bones, proxies);
    }

    [Fact]
    public void Resolve_AttachesEachProxyToItsOwnBone()
    {
        var (bones, proxies) = Destroyer();

        var particles = PreviewParticleResolver.Resolve("hull", bones, proxies, []);

        Assert.Equal(4, particles.Count);
        Assert.All(particles, p => Assert.Equal("hull", p.PartId));
        Assert.Contains(particles, p => p.SystemRef == "pe_stardestroyerengines"
                                        && p.Bone == "pe_stardestroyerengines");
    }

    [Fact]
    public void Resolve_GivesRepeatedProxiesDistinctIds()
    {
        // Twenty of the Star Destroyer's proxies share one name. Keyed on the name alone, nineteen
        // would be dropped and the ship would smoke from a single corner.
        var (bones, proxies) = Destroyer();

        var particles = PreviewParticleResolver.Resolve("hull", bones, proxies, []);

        Assert.Equal(particles.Count, particles.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void Resolve_TiesDamageSmokeToTheHardpointThatOwnsItsBone()
    {
        // The join the whole damage state rests on: Damage_Particles names a HULL bone, and the smoke
        // is every proxy whose bone's PARENT is that bone. Nothing in the XML names the proxy itself.
        var (bones, proxies) = Destroyer();
        var hardpoints = new List<PreviewHardpoint> { Hardpoint("HP_Front_Left", "HP_F-L_EmitDamage") };

        var particles = PreviewParticleResolver.Resolve("hull", bones, proxies, hardpoints);
        var smoke = particles.Where(p => p.HardpointId == "HP_Front_Left").ToList();

        Assert.Equal(2, smoke.Count);
        Assert.All(smoke, p => Assert.Equal("p_hp_imperial_damage", p.SystemRef));
        Assert.All(smoke, p => Assert.Equal(PreviewParticleGate.HardpointDestroyed, p.Gate));
    }

    [Fact]
    public void Resolve_LeavesAProxyUngatedWhenNoHardpointClaimsIt()
    {
        var (bones, proxies) = Destroyer();

        var engines = PreviewParticleResolver
            .Resolve("hull", bones, proxies, [Hardpoint("HP_Front_Left", "HP_F-L_EmitDamage")])
            .Single(p => p.SystemRef == "pe_stardestroyerengines");

        Assert.Null(engines.HardpointId);
        Assert.Equal(PreviewParticleGate.Always, engines.Gate);
    }

    [Fact]
    public void Resolve_CarriesTheModelsOwnVisibilityFlag()
    {
        // pi_damage_elec_SD00 ships invisible. It is still worth listing - a modder wants to see that
        // the effect is there and switched off - but it must not play on its own.
        var (bones, proxies) = Destroyer();

        var idle = PreviewParticleResolver.Resolve("hull", bones, proxies, [])
            .Single(p => p.SystemRef == "pi_damage_elec_SD00");

        Assert.False(idle.StartsVisible);
    }

    [Fact]
    public void Resolve_TiesEngineGlowToTheHardpointThatHidesIt()
    {
        // The bone the SHIPPED data actually names. All eleven Engine_Particles tags in foc say
        // HP_E_MAINENGINES and nothing else - and on three of the four capital ships measured, no
        // proxy hangs off that bone at all: the glow sits under the engine MESH. This test used to
        // pass "engines_big" as the tag's value, a name the corpus never contains, so the join only
        // ever worked against a fixture invented to make it work.
        var (bones, proxies) = Destroyer();
        var hardpoints = new List<PreviewHardpoint>
        {
            Hardpoint("HP_Engine", engineBone: "HP_E_MainEngines", engineDeathHides: true)
        };

        var engines = PreviewParticleResolver.Resolve("hull", bones, proxies, hardpoints)
            .Single(p => p.SystemRef == "pe_stardestroyerengines");

        Assert.Equal("HP_Engine", engines.HardpointId);
        Assert.Equal(PreviewParticleGate.HardpointAlive, engines.Gate);
    }

    [Fact]
    public void Resolve_TiesEngineGlowParentedToTheNamedBoneItself()
    {
        // The Mon Cal's mid engine IS parented to HP_E_MainEngines, unlike its other two. Both
        // shapes are in the shipped models, so both have to join.
        var (bones, proxies) = Destroyer();
        bones.Add(Bone(bones.Count, "HP_E_MainEngines", 0));
        bones.Add(Bone(bones.Count, "pe_moncalengines_mid", bones.Count - 1));
        proxies.Add(Proxy("pe_moncalengines_mid", bones.Count - 1));

        var hardpoints = new List<PreviewHardpoint>
        {
            Hardpoint("HP_Engine", engineBone: "HP_E_MainEngines", engineDeathHides: true)
        };

        var mid = PreviewParticleResolver.Resolve("hull", bones, proxies, hardpoints)
            .Single(p => p.SystemRef == "pe_moncalengines_mid");

        Assert.Equal(PreviewParticleGate.HardpointAlive, mid.Gate);
    }

    [Fact]
    public void Resolve_LeavesEngineGlowUngatedWhenDeathDoesNotHideIt()
    {
        var (bones, proxies) = Destroyer();
        var hardpoints = new List<PreviewHardpoint>
        {
            Hardpoint("HP_Engine", engineBone: "HP_E_MainEngines")
        };

        var engines = PreviewParticleResolver.Resolve("hull", bones, proxies, hardpoints)
            .Single(p => p.SystemRef == "pe_stardestroyerengines");

        Assert.Equal(PreviewParticleGate.Always, engines.Gate);
    }

    [Fact]
    public void Resolve_DoesNotClaimAnOrdinaryEffectAsAnEngineGlow()
    {
        // The engine-mesh convention must not swallow the damage smoke, which is joined by its own
        // tag and would otherwise be switched on and off by the wrong mount.
        var (bones, proxies) = Destroyer();
        var hardpoints = new List<PreviewHardpoint>
        {
            Hardpoint("HP_Engine", engineBone: "HP_E_MainEngines", engineDeathHides: true)
        };

        var smoke = PreviewParticleResolver.Resolve("hull", bones, proxies, hardpoints)
            .First(p => p.SystemRef == "p_hp_imperial_damage");

        Assert.NotEqual(PreviewParticleGate.HardpointAlive, smoke.Gate);
    }

    [Fact]
    public void Resolve_CarriesTheAltAndLodTagsOffTheProxyName()
    {
        // A proxy named p_fire_small01_ALT0 shows only at damage state 0. The levels are encoded in
        // the NAME, which the reader has already stripped into these fields.
        var (bones, proxies) = Destroyer();
        proxies.Add(Proxy("p_fire", 6, alt: 2, lod: 1));

        var tagged = PreviewParticleResolver.Resolve("hull", bones, proxies, [])
            .Single(p => p.SystemRef == "p_fire");

        Assert.Equal(2, tagged.Alt);
        Assert.Equal(1, tagged.Lod);
    }

    [Fact]
    public void Resolve_LeavesAnUntaggedProxyWithNoLevels()
    {
        // Untagged means "always", not "level zero" - the engine never touches these.
        var (bones, proxies) = Destroyer();

        var engines = PreviewParticleResolver.Resolve("hull", bones, proxies, [])
            .Single(p => p.SystemRef == "pe_stardestroyerengines");

        Assert.Null(engines.Alt);
        Assert.Null(engines.Lod);
    }

    [Fact]
    public void Resolve_CarriesTheRepairAsymmetryFlag()
    {
        // altDecreaseStayHidden is what makes a repaired hardpoint's fire stay out rather than
        // flicker back on as the damage state winds down.
        var (bones, proxies) = Destroyer();
        proxies.Add(Proxy("p_fire", 6, altDecreaseStayHidden: true, alt: 1));

        var tagged = PreviewParticleResolver.Resolve("hull", bones, proxies, [])
            .Single(p => p.SystemRef == "p_fire");

        Assert.True(tagged.AltDecreaseStayHidden);
    }

    [Fact]
    public void Resolve_IgnoresAProxyPointingPastTheEndOfTheSkeleton()
    {
        // A malformed file should cost one effect, not the whole preview.
        var (bones, proxies) = Destroyer();
        proxies.Add(Proxy("p_broken", 99));

        Assert.DoesNotContain(
            PreviewParticleResolver.Resolve("hull", bones, proxies, []),
            p => p.SystemRef == "p_broken");
    }

    [Fact]
    public void Resolve_ReportsTheBoneIndexSoRepeatedNamesStayApart()
    {
        // Boba Fett carries two `p_boba_jetpack` proxies on two bones of the same name. The client
        // keys its bones by name, so with only a name to go on it hung both effects on whichever
        // bone won the map and he fired from one jet.
        var bones = new[]
        {
            Bone(0, "ROOT"),
            Bone(1, "B_Back", 0),
            Bone(2, "p_boba_jetpack", 1),
            Bone(3, "p_boba_jetpack", 1)
        };

        var proxies = new[] { Proxy("p_boba_jetpack", 2), Proxy("p_boba_jetpack", 3) };

        var particles = PreviewParticleResolver.Resolve("hull", bones, proxies, []);

        Assert.Equal([2, 3], particles.Select(p => p.BoneIndex));
        Assert.Equal(["p_boba_jetpack", "p_boba_jetpack"], particles.Select(p => p.Bone));
    }
}
