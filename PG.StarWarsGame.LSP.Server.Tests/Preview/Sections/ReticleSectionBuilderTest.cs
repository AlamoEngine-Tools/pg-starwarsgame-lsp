// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;
using PG.StarWarsGame.LSP.Server.Preview.Sections;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview.Sections;

/// <summary>
///     The reticle section on its own (#314): nothing for a scene with no targetable hardpoint, one
///     notice per targetable type the tree's reticle table does not know.
/// </summary>
public sealed class ReticleSectionBuilderTest
{
    private static PreviewHardpoint Hardpoint(string id, string? type, bool targetable)
    {
        return new PreviewHardpoint(id, null, type, "BONE", true, targetable, 100f, null, null, null, null,
            null, null, true, null, null, null);
    }

    private static ReticleSectionBuilder Builder()
    {
        return new ReticleSectionBuilder(new FakeVariantTagSource(), TestCatalogues.English);
    }

    [Fact]
    public void Reticles_AreNull_WhenNoHardpointIsTargetable()
    {
        var problems = new List<PreviewProblem>();

        var reticles = Builder().Reticles(GameIndex.Empty,
            [Hardpoint("HP_A", "HARD_POINT_ENGINE", false), Hardpoint("HP_B", null, true)], problems);

        Assert.Null(reticles);
        Assert.Empty(problems);
    }

    [Fact]
    public void Reticles_NoticeEveryTargetableType_TheTableDoesNotKnow_Once()
    {
        var problems = new List<PreviewProblem>();

        var reticles = Builder().Reticles(GameIndex.Empty,
        [
            Hardpoint("HP_A", "HARD_POINT_ENGINE", true), Hardpoint("HP_B", "hard_point_engine", true),
            Hardpoint("HP_C", "HARD_POINT_WEAPON_LASER", true)
        ], problems);

        Assert.NotNull(reticles);
        Assert.Empty(reticles.ByType);
        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.Equal(DiagnosticIds.PreviewNoReticleForType, p.DiagnosticId));
        Assert.Contains(problems, p => p.Message.Contains("HARD_POINT_ENGINE"));
        Assert.Contains(problems, p => p.Message.Contains("HARD_POINT_WEAPON_LASER"));
    }
}
