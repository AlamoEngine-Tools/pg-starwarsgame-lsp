// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The assembled scene's particle list, against the real <c>Ev_stardestroyer.alo</c>.
/// </summary>
/// <remarks>
///     <see cref="PreviewParticleResolverTest" /> proves the join on hand-built data; this proves the
///     builder actually reads the model and puts the answer in the scene. A synthetic fixture would
///     not have caught the wiring being right and the file never being opened.
/// </remarks>
public sealed class PreviewSceneParticleTest
{
    private const string Hull = "EV_StarDestroyer.ALO";

    private static string? ModelsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "eaw", "Data")))
            dir = dir.Parent;

        var models = dir is null ? null : Path.Combine(dir.FullName, "eaw", "Data", "Art", "Models");
        return models is not null && Directory.Exists(models) ? models : null;
    }

    private static PreviewScene Destroyer(out bool available)
    {
        var models = ModelsDirectory();
        available = models is not null && File.Exists(Path.Combine(models, "Ev_stardestroyer.alo"));

        if (!available)
            return PreviewScene.NotFound("", "", new GameAssetTiers(0, false, false, 0));

        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = new[]
                {
                    new GameSymbol("Generic_Star_Destroyer", GameSymbolKind.XmlObject, "SpaceUnit",
                        new FileOrigin("file:///ships.xml", 0, 0), null, null),
                    new GameSymbol("HP_SD_Weapon_FL", GameSymbolKind.XmlObject, "HardPoint",
                        new FileOrigin("file:///hardpoints.xml", 0, 0), null, null)
                }
                .ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
                    StringComparer.OrdinalIgnoreCase)
        };

        var tags = new FakeVariantTagSource()
            .With("Generic_Star_Destroyer",
                new VariantTag("Space_Model_Name", Hull, "", 0),
                new VariantTag("HardPoints", "HP_SD_Weapon_FL", "", 0))
            .With("HP_SD_Weapon_FL",
                new VariantTag("Is_Destroyable", "Yes", "", 0),
                new VariantTag("Damage_Particles", "HP_F-L_EmitDamage", "", 0));

        var builder = new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, new DirectoryAssets(models!));

        return builder.BuildForObject("Generic_Star_Destroyer");
    }

    [Fact]
    public void BuildForObject_FindsTheHullsParticleProxies()
    {
        var scene = Destroyer(out var available);
        if (!available)
            Assert.Skip("Needs the checked-in eaw/ tree.");

        // 22 proxies: twenty p_hp_imperial_damage, one engine glow, one idle electrical effect.
        Assert.Equal(22, scene.Particles.Count);
        Assert.Equal(20, scene.Particles.Count(p => p.SystemRef == "p_hp_imperial_damage"));
        Assert.Contains(scene.Particles, p => p.SystemRef == "pe_stardestroyerengines");
    }

    [Fact]
    public void BuildForObject_ClaimsTheSmokeUnderTheHardpointsDamageBone()
    {
        var scene = Destroyer(out var available);
        if (!available)
            Assert.Skip("Needs the checked-in eaw/ tree.");

        var claimed = scene.Particles.Where(p => p.HardpointId == "HP_SD_Weapon_FL").ToList();

        // Two proxies are attached to each EmitDamage bone; only the one hardpoint is declared here.
        Assert.Equal(2, claimed.Count);
        Assert.All(claimed, p => Assert.Equal("p_hp_imperial_damage", p.SystemRef));
        Assert.All(claimed, p => Assert.Equal(PreviewParticleGate.HardpointDestroyed, p.Gate));
    }

    [Fact]
    public void BuildForObject_LeavesTheIdleEffectSwitchedOffAsTheFileHasIt()
    {
        var scene = Destroyer(out var available);
        if (!available)
            Assert.Skip("Needs the checked-in eaw/ tree.");

        var idle = Assert.Single(scene.Particles, p => p.SystemRef == "pi_damage_elec_SD00");
        Assert.False(idle.StartsVisible);
    }

    /// <summary>Serves one real directory, so the builder reads the shipped bytes.</summary>
    internal sealed class DirectoryAssets(string models) : IGameAssetResolver
    {
        public GameAssetTiers Tiers => new(1, false, false, 0);

        public GameAssetLocation? Locate(string gameRelativePath)
        {
            var full = Resolve(gameRelativePath);
            return full is null
                ? null
                : new GameAssetLocation(gameRelativePath, full, GameAssetTier.Workspace);
        }

        public byte[]? Read(string gameRelativePath)
        {
            var full = Resolve(gameRelativePath);
            return full is null ? null : File.ReadAllBytes(full);
        }

        private string? Resolve(string gameRelativePath)
        {
            var name = Path.GetFileName(gameRelativePath);
            var full = Path.Combine(models, name);

            if (File.Exists(full))
                return full;

            // The shipped files are cased differently from the XML that names them.
            return Directory.EnumerateFiles(models)
                .FirstOrDefault(f => string.Equals(
                    Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
