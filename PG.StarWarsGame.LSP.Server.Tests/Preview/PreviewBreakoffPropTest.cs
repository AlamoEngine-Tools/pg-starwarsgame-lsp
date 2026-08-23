// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     Resolving <c>Death_Breakoff_Prop</c> to the debris it actually spawns.
/// </summary>
/// <remarks>
///     167 of foc's 355 hardpoints name one, 147 of them distinct, and until now it travelled as a
///     bare string that nothing read - so a destroyed mount vanished instead of shedding wreckage.
///     The prop is an ordinary <c>SpaceProp</c> carrying its own model and a DEBRIS behaviour;
///     <c>Hardpoint_Breakoff_Star_Dest_Weapon_FL</c> is the worked example.
/// </remarks>
public sealed class PreviewBreakoffPropTest
{
    [Fact]
    public void BuildForObject_ResolvesTheBreakoffPropAndItsDebrisMotion()
    {
        var scene = Build();

        var prop = Assert.Single(scene.BreakoffProps);

        Assert.Equal("Hardpoint_Breakoff_Star_Dest_Weapon_FL", prop.Id);
        Assert.Equal("EV_StarDestroyer_HP00_L-F.alo", prop.ModelRef);
        Assert.True(prop.Resolved);

        Assert.NotNull(prop.MovementVector);
        Assert.Equal(-0.2f, prop.MovementVector!.X, 3);
        Assert.Equal(0.0f, prop.MovementVector.Y, 3);
        Assert.Equal(-0.5f, prop.MovementVector.Z, 3);

        Assert.NotNull(prop.FacingRotateVector);
        Assert.Equal(0.9f, prop.FacingRotateVector!.X, 3);

        Assert.Equal(15f, prop.MinLifetimeSeconds);
        Assert.Equal(25f, prop.MaxLifetimeSeconds);
        Assert.Equal("Space_Debris_Fire_Large_Empire", prop.AttachedParticle);
        Assert.Equal("Large_Explosion_Space_Empire", prop.DeathExplosions);
        Assert.True(prop.RemoveUponDeath);
    }

    [Fact]
    public void BuildForObject_LinksTheHardpointToItsProp()
    {
        var scene = Build();

        var hardpoint = Assert.Single(scene.Hardpoints);
        Assert.Equal("Hardpoint_Breakoff_Star_Dest_Weapon_FL", hardpoint.DeathBreakoffProp);
        Assert.Contains(scene.BreakoffProps, p =>
            p.Id.Equals(hardpoint.DeathBreakoffProp, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildForObject_ReportsAPropThatIsNamedButNotDefined()
    {
        // A modder renaming a prop and missing one hardpoint is exactly the mistake this preview
        // exists to catch - so the prop is still listed, unresolved, rather than dropped.
        var index = Index([Sym("Ship", "SpaceUnit"), Sym("HP_Gun", "HardPoint")]);
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_Gun"))
            .With("HP_Gun",
                Tag("Attachment_Bone", "HP_F-L_BONE"),
                Tag("Death_Breakoff_Prop", "Hardpoint_Breakoff_Missing"));

        var scene = Builder(index, tags, "hull.alo").BuildForObject("Ship");

        var prop = Assert.Single(scene.BreakoffProps);
        Assert.False(prop.Resolved);
        Assert.Null(prop.ModelRef);
        Assert.Contains(scene.Problems, p =>
            p.Message.Contains("Hardpoint_Breakoff_Missing", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildForObject_ListsAPropSharedByTwoHardpointsOnlyOnce()
    {
        // The Star Destroyer shares breakoff props between mirrored mounts. Two entries would have
        // the client instantiate the same wreck twice.
        var index = Index([
            Sym("Ship", "SpaceUnit"), Sym("HP_L", "HardPoint"), Sym("HP_R", "HardPoint"),
            Sym("Breakoff", "SpaceProp")
        ]);
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_L, HP_R"))
            .With("HP_L", Tag("Attachment_Bone", "A"), Tag("Death_Breakoff_Prop", "Breakoff"))
            .With("HP_R", Tag("Attachment_Bone", "B"), Tag("Death_Breakoff_Prop", "Breakoff"))
            .With("Breakoff", Tag("Space_Model_Name", "wreck.alo"));

        var scene = Builder(index, tags, "hull.alo", "wreck.alo").BuildForObject("Ship");

        Assert.Single(scene.BreakoffProps);
        Assert.Equal(2, scene.Hardpoints.Count);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static PreviewScene Build()
    {
        var index = Index([
            Sym("Generic_Star_Destroyer", "SpaceUnit"),
            Sym("HP_SD_Weapon_FL", "HardPoint"),
            Sym("Hardpoint_Breakoff_Star_Dest_Weapon_FL", "SpaceProp")
        ]);

        var tags = new FakeVariantTagSource()
            .With("Generic_Star_Destroyer",
                Tag("Space_Model_Name", "EV_StarDestroyer.ALO"),
                Tag("HardPoints", "HP_SD_Weapon_FL"))
            .With("HP_SD_Weapon_FL",
                Tag("Type", "HARD_POINT_WEAPON_LASER"),
                Tag("Attachment_Bone", "HP_F-L_BONE"),
                Tag("Model_To_Attach", "EV_StarDestroyer_HP00_L-F.alo"),
                Tag("Death_Breakoff_Prop", "Hardpoint_Breakoff_Star_Dest_Weapon_FL"))
            .With("Hardpoint_Breakoff_Star_Dest_Weapon_FL",
                Tag("Space_Model_Name", " EV_StarDestroyer_HP00_L-F.alo "),
                Tag("SpaceBehavior", " DEBRIS "),
                Tag("Debris_Movement_Vector", " -0.2, 0.0, -0.5 "),
                Tag("Debris_Facing_Rotate_Vector", " 0.9, 0.4, 0.3 "),
                Tag("Debris_Min_Lifetime_Seconds", " 15.0 "),
                Tag("Debris_Max_Lifetime_Seconds", " 25.0 "),
                Tag("Debris_Attached_Particle", " Space_Debris_Fire_Large_Empire "),
                Tag("Death_Explosions", " Large_Explosion_Space_Empire "),
                Tag("Remove_Upon_Death", "true"));

        return Builder(index, tags, "EV_StarDestroyer.ALO", "EV_StarDestroyer_HP00_L-F.alo")
            .BuildForObject("Generic_Star_Destroyer");
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

    private static GameIndex Index(IEnumerable<GameSymbol> symbols)
    {
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static PreviewSceneBuilder Builder(
        GameIndex index, FakeVariantTagSource tags, params string[] resolvableAssets)
    {
        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, new FakeAssets(resolvableAssets));
    }
}
