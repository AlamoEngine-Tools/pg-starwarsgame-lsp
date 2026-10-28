// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Preview.Sections;
using PG.StarWarsGame.LSP.Preview;

namespace PG.StarWarsGame.LSP.Preview.Tests.Sections;

/// <summary>
///     The bare-file scenes on their own (#314): a model opened directly is a one-part scene whose
///     part says whether the file was found; an animation no model's skeleton fits is a not-found scene.
/// </summary>
public sealed class ModelSceneBuilderTest
{
    private static ModelSceneBuilder Builder(FakeAssets assets)
    {
        return new ModelSceneBuilder(assets, new SchemaProviderBuilder().Build(), new FakeVariantTagSource(),
            TestCatalogues.English);
    }

    [Fact]
    public void BuildForModel_AResolvedModel_IsAOnePartModelScene()
    {
        var scene = Builder(new FakeAssets("hull.alo")).BuildForModel(GameIndex.Empty, "hull.alo");

        Assert.Equal(PreviewSceneKind.Model, scene.Kind);
        Assert.Equal("hull.alo", scene.Subject);
        var part = Assert.Single(scene.Parts);
        Assert.Equal("hull", part.Id);
        Assert.Equal(PreviewPartOrigin.Hull, part.Origin);
        Assert.True(part.Resolved);
        Assert.Empty(scene.Hardpoints);
        Assert.Empty(scene.Problems);
    }

    [Fact]
    public void BuildForModel_AMissingModel_IsStillAScene_WithTheNotFoundError()
    {
        var scene = Builder(new FakeAssets()).BuildForModel(GameIndex.Empty, "nowhere.alo");

        Assert.Equal(PreviewSceneKind.Model, scene.Kind);
        Assert.False(Assert.Single(scene.Parts).Resolved);
        var problem = Assert.Single(scene.Problems);
        Assert.Equal(DiagnosticIds.PreviewModelNotFound, problem.DiagnosticId);
        Assert.Equal("error", problem.Severity);
        Assert.NotNull(scene.Cameras);
        Assert.Empty(scene.Cameras);
    }

    [Fact]
    public void BuildForAnimation_WithNoModelToFit_IsANotFoundScene()
    {
        var scene = Builder(new FakeAssets()).BuildForAnimation(GameIndex.Empty, "orphan_idle_00.ala");

        Assert.Equal("orphan_idle_00.ala", scene.Subject);
        Assert.Empty(scene.Parts);
        var problem = Assert.Single(scene.Problems);
        Assert.Equal(DiagnosticIds.PreviewSubjectNotFound, problem.DiagnosticId);
    }

    [Fact]
    public void CamerasOf_AreEmpty_ForAFileThatIsNotAModel()
    {
        var problems = new List<PreviewProblem>();

        Assert.Empty(Builder(new FakeAssets()).CamerasOf("nowhere.alo", problems));
        Assert.Empty(problems);
    }
}