// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using PG.StarWarsGame.LSP.Assets.Models;
using static PG.StarWarsGame.LSP.Assets.Tests.Models.AloChunkFixture;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     The connections block (<c>0x600</c>): what rides which bone, plus proxies and lights.
/// </summary>
/// <remarks>
///     This is the block the assembled-unit preview leans on hardest. A hardpoint's damage smoke is
///     found by taking its <c>Damage_Particles</c> bone and collecting the proxies parented under it -
///     verified against <c>Ev_stardestroyer.alo</c>, where 19 <c>p_hp_imperial_damage</c> proxies hang
///     off bones named <c>HP_&lt;x&gt;_EmitDamage</c>. None of that works if attachment is wrong.
/// </remarks>
public sealed class AloModelReaderConnectionsTest
{
    private static byte[] TrivialSubMesh()
    {
        return SubMesh("MeshAlpha.fx", "alD3dVertNU2", [MasterVertex(Vector3.Zero)], [0, 0, 0]);
    }

    private static byte[] Skel(params string[] boneNames)
    {
        return Skeleton([
            .. boneNames.Select((n, i) => Bone(n, i == 0 ? -1 : 0, true, Translation(0, 0, 0)))
        ]);
    }

    [Fact]
    public void Read_Connections_AttachesEachMeshToItsBone()
    {
        var alo = Concat(
            Skel("ROOT", "HP_F-L_Bone", "HP_F-R_Bone"),
            Mesh("TURRET_L", [TrivialSubMesh()]),
            Mesh("TURRET_R", [TrivialSubMesh()]),
            Connections(connections: [Connection(0, 1), Connection(1, 2)]));

        var meshes = AloModelReader.Read(alo).Meshes;

        Assert.Equal(1, meshes[0].BoneIndex);
        Assert.Equal(2, meshes[1].BoneIndex);
    }

    [Fact]
    public void Read_Connections_IndexesMeshesAndLightsAsOneListInFileOrder()
    {
        // The trap: the object index spans meshes AND lights interleaved in top-level order, not the
        // meshes alone. Indexing meshes only puts every light's attachment onto the wrong object the
        // moment a model has both, which 246 shipped models do.
        var alo = Concat(
            Skel("ROOT", "BONE_A", "BONE_B", "BONE_C"),
            Mesh("HULL", [TrivialSubMesh()]),
            Light("GLOW"),
            Mesh("ENGINE", [TrivialSubMesh()]),
            Connections(connections: [Connection(0, 1), Connection(1, 2), Connection(2, 3)]));

        var model = AloModelReader.Read(alo);

        Assert.Equal(1, model.Meshes[0].BoneIndex);
        Assert.Equal(2, model.Lights[0].BoneIndex);
        Assert.Equal(3, model.Meshes[1].BoneIndex);
    }

    [Fact]
    public void Read_Connections_LeavesAnUnconnectedMeshWithoutABone()
    {
        var alo = Concat(
            Skel("ROOT"),
            Mesh("HULL", [TrivialSubMesh()]),
            Connections());

        Assert.Equal(-1, AloModelReader.Read(alo).Meshes[0].BoneIndex);
    }

    [Fact]
    public void Read_Connections_RejectsAnObjectIndexPastTheAttachableList()
    {
        var alo = Concat(
            Skel("ROOT"),
            Mesh("HULL", [TrivialSubMesh()]),
            Connections(connections: [Connection(5, 0)]));

        Assert.Throws<AloFormatException>(() => AloModelReader.Read(alo));
    }

    [Fact]
    public void Read_Connections_RejectsABoneIndexPastTheSkeleton()
    {
        var alo = Concat(
            Skel("ROOT"),
            Mesh("HULL", [TrivialSubMesh()]),
            Connections(connections: [Connection(0, 9)]));

        Assert.Throws<AloFormatException>(() => AloModelReader.Read(alo));
    }

    // ── proxies ───────────────────────────────────────────────────────────────

    [Fact]
    public void Read_Proxies_ReadsNameAndBone()
    {
        var alo = Concat(
            Skel("ROOT", "HP_F-L_EmitDamage", "p_hp_imperial_damage"),
            Connections(proxies: [Proxy("p_hp_imperial_damage", 2)]));

        var proxy = Assert.Single(AloModelReader.Read(alo).Proxies);

        Assert.Equal("p_hp_imperial_damage", proxy.Name);
        Assert.Equal(2, proxy.BoneIndex);
    }

    [Fact]
    public void Read_Proxies_TreatsTheStoredVisibilityFieldAsInverted()
    {
        var alo = Concat(
            Skel("ROOT", "A", "B", "C"),
            Connections(proxies:
            [
                Proxy("shown_by_default", 1),
                Proxy("shown_explicitly", 2, hidden: 0),
                Proxy("pi_damage_elec_SD00", 3, hidden: 1)
            ]));

        var proxies = AloModelReader.Read(alo).Proxies;

        Assert.True(proxies[0].IsVisible);
        Assert.True(proxies[1].IsVisible);
        Assert.False(proxies[2].IsVisible);
    }

    [Fact]
    public void Read_Proxies_ReadsAltDecreaseStayHidden()
    {
        // Present on 2823 of the 9412 shipped proxies. It is what stops a damage effect reappearing
        // as a unit is repaired, so the repair direction in the preview depends on it.
        var alo = Concat(
            Skel("ROOT", "A", "B"),
            Connections(proxies:
            [
                Proxy("plain", 1),
                Proxy("stays_hidden", 2, altDecreaseStayHidden: true)
            ]));

        var proxies = AloModelReader.Read(alo).Proxies;

        Assert.False(proxies[0].AltDecreaseStayHidden);
        Assert.True(proxies[1].AltDecreaseStayHidden);
    }

    [Fact]
    public void Read_Proxies_ParsesAltAndLodOutOfTheProxyName()
    {
        // Alttest.alo ships exactly this pair: one ALT-tagged proxy and one untagged.
        var alo = Concat(
            Skel("ROOT", "A", "B"),
            Connections(proxies: [Proxy("p_fire_small01_ALT0", 1), Proxy("p_fire_small01", 2)]));

        var proxies = AloModelReader.Read(alo).Proxies;

        Assert.Equal(0, proxies[0].Alt);
        Assert.Null(proxies[1].Alt);
    }

    [Fact]
    public void Read_Proxies_RejectsABoneIndexPastTheSkeleton()
    {
        var alo = Concat(Skel("ROOT"), Connections(proxies: [Proxy("orphan", 4)]));

        Assert.Throws<AloFormatException>(() => AloModelReader.Read(alo));
    }

    // ── lights ────────────────────────────────────────────────────────────────

    [Fact]
    public void Read_Lights_ReadsNameTypeColourAndAttenuation()
    {
        var alo = Concat(
            Skel("ROOT"),
            Light("ENGINE_GLOW", type: (int)AlamoLightType.Spot, color: new Vector3(1f, 0.5f, 0.25f),
                intensity: 2f, farAttenuationEnd: 100f, farAttenuationStart: 50f,
                hotspotSize: 10f, falloffSize: 20f),
            Connections());

        var light = Assert.Single(AloModelReader.Read(alo).Lights);

        Assert.Equal("ENGINE_GLOW", light.Name);
        Assert.Equal(AlamoLightType.Spot, light.Type);
        Assert.Equal(new Vector3(1f, 0.5f, 0.25f), light.Color);
        Assert.Equal(2f, light.Intensity);
        Assert.Equal(100f, light.FarAttenuationEnd);
        Assert.Equal(50f, light.FarAttenuationStart);
        Assert.Equal(10f, light.HotspotSize);
        Assert.Equal(20f, light.FalloffSize);
    }

    // ── the whole shape ───────────────────────────────────────────────────────

    [Fact]
    public void Read_ProxyUnderADamageBone_IsReachableFromThatBone()
    {
        // The join the hardpoint preview is built on, in miniature: a hardpoint's Damage_Particles tag
        // names HP_F-L_EmitDamage, and the smoke to light up is every proxy whose bone's PARENT is
        // that bone. Pinned here so a change to bone parenting cannot quietly break damage states.
        var alo = Concat(
            Skeleton(
                Bone("ROOT", -1, true, Translation(0, 0, 0)),
                Bone("HP_F-L_EmitDamage", 0, true, Translation(1, 0, 0)),
                Bone("p_hp_imperial_damage", 1, true, Translation(0, 0, 0)),
                Bone("HP_F-R_EmitDamage", 0, true, Translation(-1, 0, 0)),
                Bone("p_hp_imperial_damage", 3, true, Translation(0, 0, 0))),
            Connections(proxies:
            [
                Proxy("p_hp_imperial_damage", 2),
                Proxy("p_hp_imperial_damage", 4)
            ]));

        var model = AloModelReader.Read(alo);

        var damageBone = model.Bones.Single(b => b.Name == "HP_F-L_EmitDamage");
        var attached = model.Proxies
            .Where(p => model.Bones[p.BoneIndex].ParentIndex == damageBone.Index)
            .ToList();

        Assert.Equal(["p_hp_imperial_damage"], attached.Select(p => p.Name));
    }
}
