// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     A hardpoint that names no <c>Attachment_Bone</c>.
/// </summary>
/// <remarks>
///     <para>
///         Measured in the game (FoC x64, 2024 release): the engine attaches such a hardpoint to
///         nothing. Its world position falls back to <c>Fire_Bone_A</c> and, with that unset too,
///         to the map origin; its model is never attached; the targeting reticle consults the
///         attachment bone only and is drawn in the top-left corner of the screen. Damage reads no
///         bone, so a TARGETABLE one is hit only where the game places it - at the origin, in
///         practice never - and since a unit dies only when every targetable hardpoint is
///         destroyed, one of these makes the whole unit indestructible.
///     </para>
///     <para>
///         Two findings, both errors. They differ in consequence and in what the author has to do
///         about them, and reporting the pair under one id would have a reader suppress "hardpoint
///         placement is broken" and silence "this unit cannot be killed".
///     </para>
/// </remarks>
public sealed class PreviewHardpointAttachmentTest
{
    [Fact]
    public void TargetableHardpointWithNoAttachmentBone_IsAnError()
    {
        var scene = SceneWith(true, null);

        var problem = Assert.Single(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewTargetableHardpointNoBone);

        Assert.Equal("error", problem.Severity);
        Assert.Equal("HP_Gun", problem.HardpointId);
    }

    /// <summary>
    ///     The consequence is the point of the finding, so it has to be in the words.
    /// </summary>
    [Fact]
    public void TargetableHardpointWithNoAttachmentBone_SaysWhatItCosts()
    {
        var scene = SceneWith(true, null);

        var problem = Assert.Single(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewTargetableHardpointNoBone);

        Assert.Contains("map origin", problem.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("screen corner", problem.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be destroyed", problem.Message, StringComparison.OrdinalIgnoreCase);
        // The old wording named a "screen root" the engine has no such thing as. Measured: the
        // hardpoint is attached to nothing; its reticle lands in the screen corner.
        Assert.DoesNotContain("screen root", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Measured: the hardpoint's world position falls back to <c>Fire_Bone_A</c>, so with one
    ///     set it is hit there and the unit can die. The reticle consults the attachment bone only,
    ///     so it still lands in the screen corner - the finding stays, the consequence changes.
    /// </summary>
    [Fact]
    public void TargetableHardpointWithNoAttachmentBone_ButAFireBone_IsPlacedAtTheFireBone()
    {
        var scene = SceneWith(true, null, "HP_Gun_Fire");

        var problem = Assert.Single(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewTargetableHardpointNoBone);

        Assert.Equal("error", problem.Severity);
        Assert.Contains("Fire_Bone_A", problem.Message, StringComparison.Ordinal);
        Assert.Contains("screen corner", problem.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("map origin", problem.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cannot be destroyed", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     One finding, not two: the general case must not also fire, or the panel says the same
    ///     thing twice at two severities.
    /// </summary>
    [Fact]
    public void TargetableHardpointWithNoAttachmentBone_DoesNotAlsoRaiseTheGeneralFinding()
    {
        var scene = SceneWith(true, null);

        Assert.DoesNotContain(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewHardpointNoBone);
    }

    /// <summary>
    ///     An error too: the hardpoint is placed at the map origin and its model is never attached,
    ///     which breaks the unit whether or not anyone shoots at it.
    /// </summary>
    [Fact]
    public void UntargetableHardpointWithNoAttachmentBone_IsAnError()
    {
        var scene = SceneWith(false, null);

        var problem = Assert.Single(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewHardpointNoBone);

        Assert.Equal("error", problem.Severity);
        Assert.Contains("map origin", problem.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Model_To_Attach", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("screen root", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The old message claimed the hardpoint "sits at the hull's origin", which is not what the
    ///     engine does with it. Pinned so the corrected fact cannot quietly revert.
    /// </summary>
    [Fact]
    public void AHardpointWithNoAttachmentBone_IsNotDescribedAsSittingAtTheHullOrigin()
    {
        foreach (var targetable in new[] { true, false })
        {
            var scene = SceneWith(targetable, null);

            Assert.DoesNotContain(scene.Problems,
                p => p.Message.Contains("hull's origin", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void AHardpointThatNamesItsBone_ReportsNeitherFinding()
    {
        var scene = SceneWith(true, "HP_Gun_BONE");

        Assert.DoesNotContain(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewHardpointNoBone
                 || p.DiagnosticId == DiagnosticIds.PreviewTargetableHardpointNoBone);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static PreviewScene SceneWith(bool targetable, string? attachmentBone,
        string? fireBoneA = null)
    {
        var index = Index([Sym("Ship", "SpaceUnit"), Sym("HP_Gun", "HardPoint")]);

        var hardpointTags = new List<VariantTag>
        {
            Tag("Is_Targetable", targetable ? "Yes" : "No")
        };

        if (attachmentBone is not null)
            hardpointTags.Add(Tag("Attachment_Bone", attachmentBone));
        if (fireBoneA is not null)
            hardpointTags.Add(Tag("Fire_Bone_A", fireBoneA));

        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_Gun"))
            .With("HP_Gun", [.. hardpointTags]);

        return Builder(index, tags, "hull.alo").BuildForObject("Ship");
    }

    private static PreviewSceneBuilder Builder(
        GameIndex index, FakeVariantTagSource tags, params string[] resolvableAssets)
    {
        return new PreviewSceneBuilder(new FakeGameIndexService(index), new SchemaProviderBuilder().Build(),
            tags, new FakeAssets(resolvableAssets), ResxMessageCatalog.Server("en"));
    }

    private static GameSymbol Sym(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin($"file:///{id}.xml", 0, 0), null);
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
                s => s.Id, ImmutableArray.Create, StringComparer.OrdinalIgnoreCase)
        };
    }
}