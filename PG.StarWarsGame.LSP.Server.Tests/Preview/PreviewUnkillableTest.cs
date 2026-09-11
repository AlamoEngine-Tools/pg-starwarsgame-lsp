// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     Can this unit actually be killed?
/// </summary>
/// <remarks>
///     <para>
///         Damage is routed by the COLLISION MESH a projectile struck, matched with <c>_stricmp</c>
///         against each hardpoint's <c>Collision_Mesh</c>. A destroyable hardpoint whose value
///         matches nothing in the model can therefore never be hit - and because the all-destroyed
///         branch counts by <c>Is_Destroyable</c> and never asks whether a hardpoint was reachable,
///         one such hardpoint blocks that branch forever.
///     </para>
///     <para>
///         The value may name a MESH or a BONE: vanilla writes <c>Collision_Mesh</c> identical to
///         <c>Attachment_Bone</c> 22 times in foc and 6 in eaw, so the check is against the union of
///         both, exactly as <c>HardpointBoneNotOnModelHandler</c> does for the attachment bone.
///     </para>
///     <para>
///         Not reported where the object does not die with its hardpoints: <c>U_Ground_Palace</c>
///         keeps its generators as destructible scenery on purpose, and its hardpoints were never a
///         route to its death.
///     </para>
/// </remarks>
public sealed class PreviewUnkillableTest
{
    [Fact]
    public void ReportsADestroyableHardpointWhoseCollisionMeshMatchesNothing()
    {
        var scene = Ship(
            ("HP_Gun", [
                Tag("Health", "350"), Tag("Is_Destroyable", "Yes"),
                Tag("Attachment_Bone", "HP_GUN"), Tag("Collision_Mesh", "HP_GUN_COL")
            ]));

        var problem = Assert.Single(
            scene.Problems.Where(p => p.DiagnosticId == DiagnosticIds.PreviewHardpointUnreachable));

        Assert.Equal("error", problem.Severity);
        Assert.Contains("HP_GUN_COL", problem.Message);
    }

    [Fact]
    public void AcceptsACollisionMeshTheModelActuallyHas()
    {
        var scene = Ship(
            ("HP_Gun", [
                Tag("Health", "350"), Tag("Is_Destroyable", "Yes"),
                Tag("Attachment_Bone", "HP_GUN"), Tag("Collision_Mesh", "HP_GUN_COLL")
            ]));

        Assert.Empty(
            scene.Problems.Where(p => p.DiagnosticId == DiagnosticIds.PreviewHardpointUnreachable));
    }

    // 22 vanilla hardpoints in foc point their Collision_Mesh at their own bone - the Executor's
    // tractor mount among them - so a check against mesh names alone would fire on all of them.
    [Fact]
    public void AcceptsACollisionMeshThatNamesABone()
    {
        var scene = Ship(
            ("HP_Gun", [
                Tag("Health", "350"), Tag("Is_Destroyable", "Yes"),
                Tag("Attachment_Bone", "HP_GUN"), Tag("Collision_Mesh", "HP_GUN")
            ]));

        Assert.Empty(
            scene.Problems.Where(p => p.DiagnosticId == DiagnosticIds.PreviewHardpointUnreachable));
    }

    [Fact]
    public void ReportsADestroyableHardpointWithNoCollisionMeshAtAll()
    {
        // Zero of the 557 shipped hardpoints omit it while being destroyable. The field is never
        // filled in from the bone, so an omitted tag can never be matched.
        var scene = Ship(
            ("HP_Gun", [
                Tag("Health", "350"), Tag("Is_Destroyable", "Yes"), Tag("Attachment_Bone", "HP_GUN")
            ]));

        Assert.Single(
            scene.Problems.Where(p => p.DiagnosticId == DiagnosticIds.PreviewHardpointUnreachable));
    }

    [Fact]
    public void IgnoresAHardpointThatCouldNeverDieAnyway()
    {
        var scene = Ship(
            ("HP_Fixed", [
                Tag("Health", "350"), Tag("Is_Destroyable", "No"),
                Tag("Attachment_Bone", "HP_GUN"), Tag("Collision_Mesh", "HP_GUN_COL")
            ]));

        Assert.Empty(
            scene.Problems.Where(p => p.DiagnosticId == DiagnosticIds.PreviewHardpointUnreachable));
    }

    [Fact]
    public void SaysNothingWhereTheUnitDoesNotDieWithItsHardpoints()
    {
        // The palace. Its hardpoints were never a route to its death, so one that cannot be shot
        // takes nothing away.
        var scene = Ship(
            [Tag("Should_Be_Destroyed_When_All_Hardpoints_Destroyed", "No")],
            ("HP_Gun", [
                Tag("Health", "350"), Tag("Is_Destroyable", "Yes"),
                Tag("Attachment_Bone", "HP_GUN"), Tag("Collision_Mesh", "HP_GUN_COL")
            ]));

        Assert.Empty(
            scene.Problems.Where(p => p.DiagnosticId == DiagnosticIds.PreviewHardpointUnreachable));
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static PreviewScene Ship(params (string Id, VariantTag[] Tags)[] hardpoints)
    {
        return Ship([], hardpoints);
    }

    private static PreviewScene Ship(
        VariantTag[] extra, params (string Id, VariantTag[] Tags)[] hardpoints)
    {
        var symbols = new List<GameSymbol> { Sym("Ship", "SpaceUnit") };
        symbols.AddRange(hardpoints.Select(h => Sym(h.Id, "HardPoint")));

        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase),
            // Bones UNION mesh names, which is what the engine matches against.
            ModelBones = new Dictionary<string, ImmutableArray<string>>
            {
                [ModelBoneKey.From("hull.alo")] = ["HP_GUN", "HP_GUN_COLL"],
            }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
        };

        var shipTags = new List<VariantTag>
        {
            Tag("Space_Model_Name", "hull.alo"),
            Tag("HardPoints", string.Join(", ", hardpoints.Select(h => h.Id))),
        };
        shipTags.AddRange(extra);

        var tags = new FakeVariantTagSource().With("Ship", shipTags.ToArray());
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
