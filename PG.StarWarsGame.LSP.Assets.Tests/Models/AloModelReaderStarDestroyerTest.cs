// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     Reads one real shipped model and checks it against counts derived independently.
/// </summary>
/// <remarks>
///     <para>
///         The corpus sweep proves nothing throws; this proves the reader gets the right answer. The
///         expected numbers were obtained by walking the chunk tree in a separate script rather than
///         by running this reader, so they are an outside check rather than a snapshot of current
///         behaviour.
///     </para>
///     <para>
///         <c>Ev_stardestroyer.alo</c> is the reference model for the whole preview feature: it is the
///         hull the assembled-unit work is built against, and it is the file that established how
///         hardpoint damage effects are wired.
///     </para>
/// </remarks>
public sealed class AloModelReaderStarDestroyerTest
{
    private const string ModelName = "Ev_stardestroyer.alo";

    [Fact]
    public void Read_StarDestroyer_MatchesIndependentlyDerivedCounts()
    {
        var model = LoadOrSkip();

        Assert.Equal(71, model.Bones.Count);
        Assert.Equal(15, model.Meshes.Count);
        Assert.Equal(22, model.Proxies.Count);
        Assert.Empty(model.Lights);
        Assert.Empty(model.Dazzles);
    }

    [Fact]
    public void Read_StarDestroyer_HangsEveryDamageProxyOffAnEmitDamageBone()
    {
        // The join the assembled-unit preview depends on. A hardpoint's <Damage_Particles> tag names a
        // bone such as HP_F-L_EmitDamage, and the smoke to light up when that hardpoint dies is the
        // set of proxies parented to it. If this ever stops holding, hardpoint damage states are
        // showing the wrong effects - or none.
        var model = LoadOrSkip();

        var damageProxies = model.Proxies
            .Where(p => p.Name.Equals("p_hp_imperial_damage", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(20, damageProxies.Count);

        foreach (var proxy in damageProxies)
        {
            var parent = model.Bones[model.Bones[proxy.BoneIndex].ParentIndex];
            Assert.EndsWith("EmitDamage", parent.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Read_StarDestroyer_CarriesTheEngineGlowAndTheHiddenElectricalProxy()
    {
        var model = LoadOrSkip();

        var engines = Assert.Single(model.Proxies, p => p.Name == "pe_stardestroyerengines");
        Assert.True(engines.IsVisible);

        // Ships hidden: the engine turns it on only when the hull takes damage, which is exactly the
        // state the preview's damage controls need to be able to force.
        var electrical = Assert.Single(model.Proxies, p => p.Name == "pi_damage_elec_SD00");
        Assert.False(electrical.IsVisible);
    }

    [Fact]
    public void Read_StarDestroyer_AttachesEveryMeshToARealBone()
    {
        var model = LoadOrSkip();

        foreach (var mesh in model.Meshes)
        {
            Assert.InRange(mesh.BoneIndex, 0, model.Bones.Count - 1);
        }
    }

    [Fact]
    public void Read_StarDestroyer_ReadsGeometryAndTheShadersItNames()
    {
        var model = LoadOrSkip();

        var subMeshes = model.Meshes.SelectMany(m => m.SubMeshes).ToList();

        Assert.All(subMeshes, s => Assert.NotEmpty(s.Vertices));
        Assert.All(subMeshes, s => Assert.Equal(0, s.Indices.Count % 3));

        // The exact shader set the file holds, read out of it with a separate script. Asserted whole
        // rather than by sampling, because it also pins the sub-mesh count per shader - and it shows
        // the range the material layer has to cope with on a single hull: a colorize variant, plain
        // alpha, a shadow volume, additive glows, the shield, and the engine's default fallback.
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["MeshAlpha.fx"] = 9,
                ["MeshAdditive.fx"] = 2,
                ["MeshBumpColorize.fx"] = 1,
                ["MeshShadowVolume.fx"] = 1,
                ["MeshShield.fx"] = 1,
                ["alDefault.fx"] = 1
            },
            subMeshes.GroupBy(s => s.Shader).ToDictionary(g => g.Key, g => g.Count()));
    }

    private static AlamoModelContent LoadOrSkip()
    {
        var path = FindModel();
        if (path is null)
            Assert.Skip($"{ModelName} is not present; the extracted game tree is missing.");

        return AloModelReader.Read(File.ReadAllBytes(path));
    }

    private static string? FindModel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "eaw", "Data", "Art", "Models", ModelName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
