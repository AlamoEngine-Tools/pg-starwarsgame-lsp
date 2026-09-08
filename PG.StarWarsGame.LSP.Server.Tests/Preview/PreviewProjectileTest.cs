// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The projectiles a subject's weapons actually name.
/// </summary>
/// <remarks>
///     173 exist in foc and 106 in eaw, so the scene carries only what this subject fires rather
///     than the catalogue. They inherit heavily - 54 of foc's use
///     <c>Variant_Of_Existing_Type</c> - so a bolt's damage usually lives on its generic template
///     rather than on the entry the weapon names.
/// </remarks>
public sealed class PreviewProjectileTest
{
    [Fact]
    public void BuildForObject_ResolvesTheProjectileAWeaponNames()
    {
        var projectile = Assert.Single(Build().Projectiles);

        Assert.Equal("Proj_Ship_Small_Laser_Cannon_Red", projectile.Id);
        Assert.Equal("W_LASER_SMALL.ALO", projectile.ModelFile);
        Assert.Equal(5f, projectile.Damage);
    }

    [Fact]
    public void BuildForObject_InheritsThroughTheVariantChain()
    {
        // The Red bolt declares only its model, texture slot and damage; speed, category, flight
        // distance and every damage switch come from the Generic template it varies.
        var projectile = Assert.Single(Build().Projectiles);

        Assert.Equal(11f, projectile.Speed);
        Assert.Equal(500f, projectile.MaxFlightDistance);
        Assert.Equal(0f, projectile.MaxRateOfTurn);
        Assert.True(projectile.DoesShieldDamage);
        Assert.False(projectile.DoesEnergyDamage);
        Assert.True(projectile.DoesHitpointDamage);
    }

    [Fact]
    public void BuildForObject_CarriesTheImpactWiring()
    {
        var projectile = Assert.Single(Build().Projectiles);

        Assert.Equal("Small_Damage_Space", projectile.DetonationParticles);
        Assert.Equal("Projectile_Shield_Absorb_Small", projectile.ShieldAbsorbParticles);
        Assert.Equal("SFX_Small_Damage_Detonation", projectile.DetonateSfxEvent);
    }

    [Fact]
    public void BuildForObject_ReadsTheCustomQuadShape()
    {
        var projectile = Assert.Single(Build().Projectiles);

        // Custom_Render and a model are NOT exclusive - 40 of foc's 63 custom-rendered projectiles
        // name one anyway - so the flag decides how it draws and the model travels regardless.
        Assert.Equal(PreviewProjectileRender.CustomQuad, projectile.Render);
        Assert.Equal(1f, projectile.Width);
        Assert.Equal(6f, projectile.Length);
        Assert.Equal("0,0", projectile.TextureSlot);
        Assert.NotNull(projectile.ModelFile);
    }

    [Fact]
    public void BuildForObject_ReportsAModelBackedProjectileAsSuch()
    {
        var scene = Build(("Projectile_Custom_Render", "0"));

        Assert.Equal(PreviewProjectileRender.Model, Assert.Single(scene.Projectiles).Render);
    }

    [Fact]
    public void BuildForObject_ListsAProjectileSharedByTwoWeaponsOnlyOnce()
    {
        var index = Index([
            Sym("Ship", "SpaceUnit"), Sym("HP_L", "HardPoint"), Sym("HP_R", "HardPoint"),
            Sym("Proj", "Projectile")
        ]);
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_L, HP_R"))
            .With("HP_L", Tag("Fire_Bone_A", "FP_00"), Tag("Fire_Projectile_Type", "Proj"))
            .With("HP_R", Tag("Fire_Bone_A", "FP_01"), Tag("Fire_Projectile_Type", "Proj"))
            .With("Proj", Tag("Projectile_Damage", "5"));

        var scene = Builder(index, tags).BuildForObject("Ship");

        Assert.Single(scene.Projectiles);
        Assert.Equal(2, scene.Weapons.Count);
    }

    [Fact]
    public void BuildForObject_ReportsAProjectileThatIsNamedButNotDefined()
    {
        var index = Index([Sym("Ship", "SpaceUnit"), Sym("HP_L", "HardPoint")]);
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_L"))
            .With("HP_L", Tag("Fire_Bone_A", "FP_00"), Tag("Fire_Projectile_Type", "Proj_Missing"));

        var scene = Builder(index, tags).BuildForObject("Ship");

        Assert.Empty(scene.Projectiles);
        Assert.Contains(scene.Problems, p =>
            p.Message.Contains("Proj_Missing", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildForObject_CarriesNoProjectilesForAnUnarmedSubject()
    {
        var index = Index([Sym("Ship", "SpaceUnit")]);
        var tags = new FakeVariantTagSource().With("Ship", Tag("Space_Model_Name", "hull.alo"));

        Assert.Empty(Builder(index, tags).BuildForObject("Ship").Projectiles);
    }

    [Fact]
    public void BuildForObject_CarriesTheWholeBlastArea()
    {
        // Measured over foc: 63 projectiles declare Projectile_Blast_Area_Damage and 62 a _Range,
        // but only 10 declare _Dropoff with _Dropoff_Tiers, and _Max_Victims appears on 2. Flat
        // damage inside a radius is the norm; tiered falloff is the exception, and both have to
        // travel or the attacker panel cannot tell a grenade from a turbolaser.
        var scene = Build(
            ("Projectile_Blast_Area_Damage", "120.0"),
            ("Projectile_Blast_Area_Range", "250.0"),
            ("Projectile_Blast_Area_Max_Victims", "4"),
            ("Projectile_Blast_Area_Dropoff", "True"),
            ("Projectile_Blast_Area_Dropoff_Tiers", "3"));

        var projectile = Assert.Single(scene.Projectiles);

        Assert.Equal(120f, projectile.BlastAreaDamage);
        Assert.Equal(250f, projectile.BlastAreaRange);
        Assert.Equal(4, projectile.BlastAreaMaxVictims);
        Assert.True(projectile.BlastAreaDropoff);
        Assert.Equal(3, projectile.BlastAreaDropoffTiers);
    }

    [Fact]
    public void BuildForObject_LeavesASingleTargetProjectileWithNoBlastArea()
    {
        // The overwhelming majority. A bolt with no blast hits exactly one mount.
        var projectile = Assert.Single(Build().Projectiles);

        Assert.Null(projectile.BlastAreaRange);
        Assert.Null(projectile.BlastAreaMaxVictims);
        Assert.False(projectile.BlastAreaDropoff);
        Assert.Null(projectile.BlastAreaDropoffTiers);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static PreviewScene Build(params (string Name, string Value)[] overrides)
    {
        var index = Index([
            Sym("Ship", "SpaceUnit"), Sym("HP_Gun", "HardPoint"),
            Sym("Proj_Ship_Small_Laser_Cannon_Generic", "Projectile"),
            SymVariant("Proj_Ship_Small_Laser_Cannon_Red", "Projectile",
                "Proj_Ship_Small_Laser_Cannon_Generic")
        ]);

        var red = new List<VariantTag>
        {
            Tag("Space_Model_Name", "W_LASER_SMALL.ALO"),
            Tag("Projectile_Texture_Slot", "0,0"),
            Tag("Projectile_Damage", "5.0")
        };
        red.AddRange(overrides.Select(o => Tag(o.Name, o.Value)));

        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_Gun"))
            .With("HP_Gun",
                Tag("Fire_Bone_A", "FP_00"),
                Tag("Fire_Projectile_Type", "Proj_Ship_Small_Laser_Cannon_Red"))
            .With("Proj_Ship_Small_Laser_Cannon_Generic",
                Tag("Projectile_Custom_Render", "1"),
                Tag("Projectile_Width", "1.0"),
                Tag("Projectile_Length", "6.0"),
                Tag("Max_Speed", "11.0"),
                Tag("Max_Rate_Of_Turn", "0.0"),
                Tag("Projectile_Category", "Laser"),
                Tag("Projectile_Max_Flight_Distance", "500.0"),
                Tag("Projectile_Does_Shield_Damage", "Yes"),
                Tag("Projectile_Does_Energy_Damage", "No"),
                Tag("Projectile_Does_Hitpoint_Damage", "Yes"),
                Tag("Projectile_Object_Detonation_Particle", "Small_Damage_Space"),
                Tag("Projectile_Absorbed_By_Shields_Particle", "Projectile_Shield_Absorb_Small"),
                Tag("Projectile_SFXEvent_Detonate", " SFX_Small_Damage_Detonation "))
            .With("Proj_Ship_Small_Laser_Cannon_Red", red.ToArray());

        return Builder(index, tags).BuildForObject("Ship");
    }

    private static GameSymbol Sym(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin($"file:///{id}.xml", 0, 0), null, null);
    }

    private static GameSymbol SymVariant(string id, string typeName, string baseId)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin($"file:///{id}.xml", 0, 0), null, baseId);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    private static GameIndex Index(IEnumerable<GameSymbol> symbols)
    {
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static PreviewSceneBuilder Builder(GameIndex index, FakeVariantTagSource tags)
    {
        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, new FakeAssets("hull.alo", "W_LASER_SMALL.ALO"));
    }
}
