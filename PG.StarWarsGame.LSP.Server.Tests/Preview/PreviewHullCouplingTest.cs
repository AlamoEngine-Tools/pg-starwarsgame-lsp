// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The two facts the client needs to model destruction the way the engine does.
/// </summary>
/// <remarks>
///     <para>
///         Decompiled from the 2018 build: the hull and the combined hardpoint health are separate
///         pools, each capped at the other's percentage plus
///         <c>Hull_Vs_Hard_Points_Health_Constraint</c>, and the half that pulls the HULL toward the
///         hardpoints is gated by <c>Should_Be_Destroyed_When_All_Hardpoints_Destroyed</c> - the same
///         tag that gates dying when the last hardpoint does. Neither reached the client, so the
///         preview summed the hardpoints and called that the hull.
///     </para>
///     <para>
///         The constraint is a GameConstants global, so it is read rather than assumed: a mod that
///         raises it to 1 switches both corrections off entirely, and a preview drawing vanilla's 0.2
///         would show that author a leash their game does not have.
///     </para>
/// </remarks>
public sealed class PreviewHullCouplingTest
{
    [Fact]
    public void Defence_DiesWithHardpointsIsTrueWhenTheTagIsAbsent()
    {
        // Vanilla writes the tag on exactly one object, so absent has to mean the ordinary case.
        var scene = Ship(
            [Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_A")],
            ("HP_A", [Tag("Health", "350"), Tag("Is_Destroyable", "Yes")]));

        Assert.True(scene.Defence!.DiesWithHardpoints);
    }

    [Fact]
    public void Defence_DiesWithHardpointsIsFalseWhenTheTagSaysNo()
    {
        // U_Ground_Palace - its generators are destructible scenery, not a health bar.
        var scene = Ship(
            [
                Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_A"),
                Tag("Should_Be_Destroyed_When_All_Hardpoints_Destroyed", "No")
            ],
            ("HP_A", [Tag("Health", "350"), Tag("Is_Destroyable", "Yes")]));

        Assert.False(scene.Defence!.DiesWithHardpoints);
    }

    [Fact]
    public void Defence_ReadsTheConstraintFromGameConstants()
    {
        // EaWX ships 1, which switches both corrections off. The preview has to show that.
        var scene = Scene(
            [Tag("Hull_Vs_Hard_Points_Health_Constraint", "1")],
            Tag("Space_Model_Name", "hull.alo"), Tag("Tactical_Health", "400"));

        Assert.Equal(1f, scene.Defence!.HullVsHardpointsConstraint);
    }

    [Fact]
    public void Defence_FallsBackToTheShippedConstraintWhenGameConstantsIsSilent()
    {
        var scene = Scene(Tag("Space_Model_Name", "hull.alo"), Tag("Tactical_Health", "400"));

        Assert.Equal(0.2f, scene.Defence!.HullVsHardpointsConstraint);
    }

    // A value the engine could not parse leaves the constant at its default rather than at zero -
    // zero would tie the two pools together exactly, which is the opposite of doing nothing.
    [Fact]
    public void Defence_IgnoresAConstraintThatIsNotANumber()
    {
        var scene = Scene(
            [Tag("Hull_Vs_Hard_Points_Health_Constraint", "banana")],
            Tag("Space_Model_Name", "hull.alo"), Tag("Tactical_Health", "400"));

        Assert.Equal(0.2f, scene.Defence!.HullVsHardpointsConstraint);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static PreviewScene Scene(params VariantTag[] shipTags)
    {
        return Scene([], shipTags);
    }

    private static PreviewScene Scene(VariantTag[] constants, params VariantTag[] shipTags)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = new[] { Sym("Ship", "SpaceUnit") }.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };

        var tags = new FakeVariantTagSource().With("Ship", shipTags);
        if (constants.Length > 0)
            tags = tags.With("GameConstants", constants);

        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, new FakeAssets("hull.alo")).BuildForObject("Ship");
    }

    private static PreviewScene Ship(
        VariantTag[] shipTags, params (string Id, VariantTag[] Tags)[] hardpoints)
    {
        var symbols = new List<GameSymbol> { Sym("Ship", "SpaceUnit") };
        symbols.AddRange(hardpoints.Select(h => Sym(h.Id, "HardPoint")));

        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };

        var tags = new FakeVariantTagSource().With("Ship", shipTags);
        foreach (var hardpoint in hardpoints)
            tags = tags.With(hardpoint.Id, hardpoint.Tags);

        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, new FakeAssets("hull.alo")).BuildForObject("Ship");
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
