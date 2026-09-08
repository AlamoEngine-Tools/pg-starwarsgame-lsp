// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     A hardpoint that names no <c>Attachment_Bone</c>.
/// </summary>
/// <remarks>
///     <para>
///         TOLD by the user, not measured here: the engine attaches such a hardpoint to the SCREEN
///         root rather than to the object's root. For a hardpoint nobody shoots at that is merely
///         wrong placement, but a TARGETABLE one cannot then be hit at all - and since a unit dies
///         only when every one of its targetable hardpoints is destroyed, one of these makes the
///         whole unit indestructible.
///     </para>
///     <para>
///         So the two cases are two findings. They differ in severity, in consequence and in what
///         the author has to do about them, and reporting the pair under one id would have a reader
///         suppress "hardpoint placement is sloppy" and silence "this unit cannot be killed".
///     </para>
/// </remarks>
public sealed class PreviewHardpointAttachmentTest
{
    [Fact]
    public void TargetableHardpointWithNoAttachmentBone_IsAnError()
    {
        var scene = SceneWith(targetable: true, attachmentBone: null);

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
        var scene = SceneWith(targetable: true, attachmentBone: null);

        var problem = Assert.Single(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewTargetableHardpointNoBone);

        Assert.Contains("screen root", problem.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("destroy", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     One finding, not two: the general case must not also fire, or the panel says the same
    ///     thing twice at two severities.
    /// </summary>
    [Fact]
    public void TargetableHardpointWithNoAttachmentBone_DoesNotAlsoRaiseTheGeneralFinding()
    {
        var scene = SceneWith(targetable: true, attachmentBone: null);

        Assert.DoesNotContain(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewHardpointNoBone);
    }

    [Fact]
    public void UntargetableHardpointWithNoAttachmentBone_StaysAWarning()
    {
        var scene = SceneWith(targetable: false, attachmentBone: null);

        var problem = Assert.Single(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewHardpointNoBone);

        Assert.Equal("warning", problem.Severity);
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
            var scene = SceneWith(targetable, attachmentBone: null);

            Assert.DoesNotContain(scene.Problems,
                p => p.Message.Contains("hull's origin", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void AHardpointThatNamesItsBone_ReportsNeitherFinding()
    {
        var scene = SceneWith(targetable: true, attachmentBone: "HP_Gun_BONE");

        Assert.DoesNotContain(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewHardpointNoBone
                 || p.DiagnosticId == DiagnosticIds.PreviewTargetableHardpointNoBone);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static PreviewScene SceneWith(bool targetable, string? attachmentBone)
    {
        var index = Index([Sym("Ship", "SpaceUnit"), Sym("HP_Gun", "HardPoint")]);

        var hardpointTags = new List<VariantTag>
        {
            Tag("Is_Targetable", targetable ? "Yes" : "No")
        };

        if (attachmentBone is not null)
            hardpointTags.Add(Tag("Attachment_Bone", attachmentBone));

        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_Gun"))
            .With("HP_Gun", hardpointTags.ToArray());

        return Builder(index, tags, "hull.alo").BuildForObject("Ship");
    }

    private static PreviewSceneBuilder Builder(
        GameIndex index, FakeVariantTagSource tags, params string[] resolvableAssets)
    {
        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, new FakeAssets(resolvableAssets));
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
}
