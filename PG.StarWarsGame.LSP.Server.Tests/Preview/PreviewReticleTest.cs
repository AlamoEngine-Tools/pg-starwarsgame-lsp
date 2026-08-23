// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The targeting reticles the game draws over a ship's hardpoints.
/// </summary>
/// <remarks>
///     GameConstants maps seven state textures per hardpoint type - 91 rows over 13 types in foc, 77
///     over 11 in eaw - and they collapse onto five artwork families, so the wire carries five icons
///     rather than thirteen. Reading them at all depends on
///     <see cref="RepeatedTagReader" />: the effective-object resolver collapses a repeated tag to
///     its last occurrence, which would leave exactly one of the 91 rows.
/// </remarks>
public sealed class PreviewReticleTest
{
    [Fact]
    public void Reticles_MapEveryStateOfATypeOntoItsIcons()
    {
        var map = Build();

        var engine = map.ForType("HARD_POINT_ENGINE");
        Assert.NotNull(engine);
        Assert.Equal("I_Hard_Point_Reticle_Engines", engine!.Enemy);
        Assert.Equal("I_Hard_Point_Reticle_Engines_Tracked", engine.EnemyTracked);
        Assert.Equal("I_Hard_Point_Reticle_Engines", engine.Friendly);
        Assert.Equal("I_Hard_Point_Reticle_Engines_Tracked", engine.FriendlyTracked);
        Assert.Equal("I_Hard_Point_Reticle_Engines_Repair", engine.FriendlyRepairing);
    }

    [Fact]
    public void Reticles_AreMatchedCaseInsensitively()
    {
        // The shipped file is not consistent about the casing of a hardpoint type.
        Assert.NotNull(Build().ForType("hard_point_engine"));
    }

    [Fact]
    public void Reticles_ListEachDistinctIconOnce()
    {
        // Thirteen types share five artwork families; one entry per type would send the same PNG
        // over and over.
        var map = Build();

        Assert.Equal(
            ["I_Hard_Point_Reticle_Engines", "I_Hard_Point_Reticle_Engines_Repair",
                "I_Hard_Point_Reticle_Engines_Tracked", "I_Hard_Point_Reticle_Weapons",
                "I_Hard_Point_Reticle_Weapons_Repair", "I_Hard_Point_Reticle_Weapons_Tracked"],
            map.IconNames.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Reticles_CarryTheScreenSizes()
    {
        var map = Build();

        Assert.Equal(0.03f, map.EnemyScreenSize);
        Assert.Equal(0.03f, map.FriendlyScreenSize);
    }

    [Fact]
    public void Reticles_AreEmptyWhenGameConstantsIsAbsent()
    {
        // A workspace with no GameConstants of its own and no baseline is a legitimate state during
        // startup, not an error worth a diagnostic.
        var map = PreviewReticleMap.From(GameIndex.Empty, new FakeVariantTagSource());

        Assert.Empty(map.IconNames);
        Assert.Null(map.ForType("HARD_POINT_ENGINE"));
    }

    [Fact]
    public void Reticles_IgnoreAMalformedRow()
    {
        // A row missing its icon is dropped rather than registered as a type with a blank texture,
        // which would draw nothing and look like a missing file.
        var tags = new FakeVariantTagSource().With("GameConstants",
            Tag("HardPoint_Target_Reticle_Enemy_Texture", "HARD_POINT_ENGINE"),
            Tag("HardPoint_Target_Reticle_Enemy_Texture", "HARD_POINT_SHIELD_GENERATOR, I_Shield"));

        var map = PreviewReticleMap.From(GameIndex.Empty, tags);

        Assert.Null(map.ForType("HARD_POINT_ENGINE"));
        Assert.NotNull(map.ForType("HARD_POINT_SHIELD_GENERATOR"));
    }

    [Fact]
    public void BuildForObject_MarksAHardpointTargetableFromItsOwnTag()
    {
        // 258 of foc's 355 hardpoints say Yes and 97 say No; none omit it. A mount that is not
        // targetable gets no reticle, which is the honest picture rather than a decoration.
        var index = Index([Sym("Ship", "SpaceUnit"), Sym("HP_Gun", "HardPoint"), Sym("HP_Dummy", "HardPoint")]);
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_Gun, HP_Dummy"))
            .With("HP_Gun", Tag("Type", "HARD_POINT_WEAPON_LASER"), Tag("Is_Targetable", "Yes"))
            .With("HP_Dummy", Tag("Type", "HARD_POINT_DUMMY_ART"), Tag("Is_Targetable", "No"));

        var scene = new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, new FakeAssets("hull.alo")).BuildForObject("Ship");

        Assert.True(scene.Hardpoints.Single(h => h.Id == "HP_Gun").IsTargetable);
        Assert.False(scene.Hardpoints.Single(h => h.Id == "HP_Dummy").IsTargetable);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static GameSymbol Sym(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin($"file:///{id}.xml", 0, 0), null, null);
    }

    private static GameIndex Index(IEnumerable<GameSymbol> symbols)
    {
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };
    }


    /// <summary>Two of the thirteen types, with every state row the shipped file carries.</summary>
    private static PreviewReticleMap Build()
    {
        var tags = new FakeVariantTagSource().With("GameConstants",
            Tag("HardPoint_Target_Reticle_Enemy_Screen_Size", " 0.03 "),
            Tag("HardPoint_Target_Reticle_Friendly_Screen_Size", " 0.03 "),

            Tag("HardPoint_Target_Reticle_Enemy_Texture", " HARD_POINT_ENGINE, I_Hard_Point_Reticle_Engines "),
            Tag("HardPoint_Target_Reticle_Enemy_Tracked_Texture", " HARD_POINT_ENGINE, I_Hard_Point_Reticle_Engines_Tracked "),
            Tag("HardPoint_Target_Reticle_Friendly_Texture", " HARD_POINT_ENGINE, I_Hard_Point_Reticle_Engines "),
            Tag("HardPoint_Target_Reticle_Friendly_Tracked_Texture", " HARD_POINT_ENGINE, I_Hard_Point_Reticle_Engines_Tracked "),
            Tag("HardPoint_Target_Reticle_Friendly_Repairing_Texture", " HARD_POINT_ENGINE, I_Hard_Point_Reticle_Engines_Repair "),
            Tag("HardPoint_Target_Reticle_Friendly_Disabled_Texture", " HARD_POINT_ENGINE, I_Hard_Point_Reticle_Engines "),
            Tag("HardPoint_Target_Reticle_Friendly_Disabled_Tracked_Texture", " HARD_POINT_ENGINE, I_Hard_Point_Reticle_Engines_Tracked "),

            Tag("HardPoint_Target_Reticle_Enemy_Texture", " HARD_POINT_WEAPON_LASER, I_Hard_Point_Reticle_Weapons "),
            Tag("HardPoint_Target_Reticle_Enemy_Tracked_Texture", " HARD_POINT_WEAPON_LASER, I_Hard_Point_Reticle_Weapons_Tracked "),
            Tag("HardPoint_Target_Reticle_Friendly_Texture", " HARD_POINT_WEAPON_LASER, I_Hard_Point_Reticle_Weapons "),
            Tag("HardPoint_Target_Reticle_Friendly_Tracked_Texture", " HARD_POINT_WEAPON_LASER, I_Hard_Point_Reticle_Weapons_Tracked "),
            Tag("HardPoint_Target_Reticle_Friendly_Repairing_Texture", " HARD_POINT_WEAPON_LASER, I_Hard_Point_Reticle_Weapons_Repair "),
            Tag("HardPoint_Target_Reticle_Friendly_Disabled_Texture", " HARD_POINT_WEAPON_LASER, I_Hard_Point_Reticle_Weapons "),
            Tag("HardPoint_Target_Reticle_Friendly_Disabled_Tracked_Texture", " HARD_POINT_WEAPON_LASER, I_Hard_Point_Reticle_Weapons_Tracked "));

        return PreviewReticleMap.From(GameIndex.Empty, tags);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }
}
