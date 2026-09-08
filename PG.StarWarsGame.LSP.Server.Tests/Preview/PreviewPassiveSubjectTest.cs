// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     What a PASSIVE subject brings with it: its own clips and its own effects.
/// </summary>
/// <remarks>
///     <para>
///         A death clone and a breakoff prop are their own models, not further pieces of the one
///         being previewed. Everything on the wire described the SUBJECT, so the client had nothing
///         to ask for on their behalf: the host baked the subject's clip list into every GLB it
///         fetched, and the particle list only ever held the subject's proxies.
///     </para>
///     <para>
///         The clip half worked BY ACCIDENT and only sometimes. A clone's model is conventionally
///         the hull's name plus a suffix - <c>Ev_stardestroyer_d.alo</c> beside
///         <c>Ev_stardestroyer.alo</c> - so <c>ev_stardestroyer_d_die_00.ala</c> matches the hull's
///         stem prefix and rode along in the hull's list. A clone named anything else got no clip at
///         all, and no prop ever got one.
///     </para>
///     <para>
///         The effect half never worked: <c>Ev_stardestroyer_d.alo</c> carries eight proxies of its
///         own - the explosions, the fire smoke and the debris trails that ARE the death - and not
///         one of them reached the client.
///     </para>
/// </remarks>
public sealed class PreviewPassiveSubjectTest
{
    [Fact]
    public void DeathClone_CarriesTheClipsOfItsOwnModel_NotTheSubjects()
    {
        // The hull's stem prefixes the clone's, so the hull's own list picks up the clone's death
        // clip as well. That is the accident this replaces: the clone's list is its model's.
        var scene = Scene(
            [
                "data/art/models/ev_stardestroyer_idle_00.ala",
                "data/art/models/ev_stardestroyer_d_die_00.ala"
            ],
            [Sym("Ship", "SpaceUnit"), Sym("Wreck", "SpaceUnit")],
            tags => tags.With("Wreck", Tag("Space_Model_Name", "Ev_stardestroyer_d.alo")),
            Tag("Space_Model_Name", "Ev_stardestroyer.alo"),
            Tag("Death_Clone", "Damage_Normal, Wreck"));

        Assert.Equal(
            ["ev_stardestroyer_d_die_00.ala"],
            Assert.Single(scene.DeathClones).Animations);
    }

    [Fact]
    public void DeathClone_CarriesNoClipsWhenItResolvesToNoModel()
    {
        var scene = Scene(
            ["data/art/models/ev_stardestroyer_idle_00.ala"],
            [Sym("Ship", "SpaceUnit"), Sym("Wreck", "SpaceUnit")],
            tags => tags,
            Tag("Space_Model_Name", "Ev_stardestroyer.alo"),
            Tag("Death_Clone", "Damage_Normal, Wreck"));

        Assert.Empty(Assert.Single(scene.DeathClones).Animations);
    }

    [Fact]
    public void BreakoffProp_CarriesTheClipsOfItsOwnModel()
    {
        // A prop's model shares nothing with the hull's name, so it never rode along at all.
        var scene = Scene(
            [
                "data/art/models/ev_stardestroyer_idle_00.ala",
                "data/art/models/ev_stardestroyer_dead_die_00.ala"
            ],
            [Sym("Ship", "SpaceUnit"), Sym("Mount", "HardPoint"), Sym("Debris", "SpaceProp")],
            tags => tags
                .With("Mount", Tag("Is_Destroyable", "Yes"), Tag("Death_Breakoff_Prop", "Debris"))
                .With("Debris", Tag("Space_Model_Name", "Ev_stardestroyer_dead.alo")),
            Tag("Space_Model_Name", "Ev_stardestroyer.alo"),
            Tag("HardPoints", "Mount"));

        Assert.Equal(
            ["ev_stardestroyer_dead_die_00.ala"],
            Assert.Single(scene.BreakoffProps).Animations);
    }

    // ── against the shipped models ────────────────────────────────────────────

    [Fact]
    public void DeathClone_CarriesItsModelsOwnParticleProxies()
    {
        var scene = Destroyer(out var available);
        if (!available)
            Assert.Skip("Needs the checked-in eaw/ tree.");

        var clone = Assert.Single(scene.DeathClones);

        // What dying actually looks like, and none of it was on the wire.
        Assert.Contains(clone.Particles, p => p.SystemRef == "p_imperial_explosion_big00");
        Assert.Contains(clone.Particles, p => p.SystemRef == "p_firesmoke00");

        // And they are the CLONE's, not the hull's: the hull carries none of these.
        Assert.DoesNotContain(scene.Particles, p => p.SystemRef == "p_firesmoke00");
    }

    [Fact]
    public void DeathClone_ParticleIdsAreUniqueWithinItsOwnDescriptor()
    {
        var scene = Destroyer(out var available);
        if (!available)
            Assert.Skip("Needs the checked-in eaw/ tree.");

        var clone = Assert.Single(scene.DeathClones);

        Assert.Equal(clone.Particles.Count, clone.Particles.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void BreakoffProp_CarriesItsModelsOwnParticleProxies()
    {
        var scene = Destroyer(out var available);
        if (!available)
            Assert.Skip("Needs the checked-in eaw/ tree.");

        var prop = Assert.Single(scene.BreakoffProps);

        Assert.Contains(prop.Particles, p => p.SystemRef == "p_smokedeath00");
    }

    [Fact]
    public void PassiveSubjectParticles_AreNotGatedByTheSubjectsHardpoints()
    {
        // The gate joins a proxy to the hardpoint whose `Damage_Particles` bone is its parent, and
        // that is a fact about the SUBJECT's skeleton. A clone has its own, so nothing on it may be
        // claimed by a mount of the ship it replaces.
        var scene = Destroyer(out var available);
        if (!available)
            Assert.Skip("Needs the checked-in eaw/ tree.");

        Assert.All(Assert.Single(scene.DeathClones).Particles,
            p => Assert.Null(p.HardpointId));
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private const string Models = "eaw";

    private static string? ModelsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, Models, "Data")))
            dir = dir.Parent;

        var models = dir is null ? null : Path.Combine(dir.FullName, Models, "Data", "Art", "Models");
        return models is not null && Directory.Exists(models) ? models : null;
    }

    /// <summary>
    ///     A Star Destroyer with one destroyable mount, its death clone and its breakoff prop, read
    ///     out of the shipped tree so the proxies are the real ones.
    /// </summary>
    private static PreviewScene Destroyer(out bool available)
    {
        var models = ModelsDirectory();
        available = models is not null
            && File.Exists(Path.Combine(models, "Ev_stardestroyer_d.alo"))
            && File.Exists(Path.Combine(models, "Ev_stardestroyer_dead.alo"));

        if (!available)
            return PreviewScene.NotFound("", "", new GameAssetTiers(0, false, false, 0));

        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = new[]
                {
                    Sym("Ship", "SpaceUnit"), Sym("Mount", "HardPoint"),
                    Sym("Wreck", "SpaceUnit"), Sym("Debris", "SpaceProp")
                }
                .ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
                    StringComparer.OrdinalIgnoreCase)
        };

        var tags = new FakeVariantTagSource()
            .With("Ship",
                Tag("Space_Model_Name", "Ev_stardestroyer.alo"),
                Tag("HardPoints", "Mount"),
                Tag("Death_Clone", "Damage_Normal, Wreck"))
            .With("Mount",
                Tag("Is_Destroyable", "Yes"),
                Tag("Damage_Particles", "HP_F-L_EmitDamage"),
                Tag("Death_Breakoff_Prop", "Debris"))
            .With("Wreck", Tag("Space_Model_Name", "Ev_stardestroyer_d.alo"))
            .With("Debris", Tag("Space_Model_Name", "Ev_stardestroyer_dead.alo"));

        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
                tags, new PreviewSceneParticleTest.DirectoryAssets(models!))
            .BuildForObject("Ship");
    }

    private static PreviewScene Scene(
        string[] assetFiles,
        GameSymbol[] symbols,
        Func<FakeVariantTagSource, FakeVariantTagSource> extra,
        params VariantTag[] shipTags)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase),
            AssetFiles = MergedAssetFileIndex.Merge([], assetFiles)
        };

        var tags = extra(new FakeVariantTagSource().With("Ship", shipTags));

        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, new FakeAssets("Ev_stardestroyer.alo")).BuildForObject("Ship");
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
