// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;
using PG.StarWarsGame.LSP.Server.Preview.Sections;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview.Sections;

/// <summary>
///     The animation section on its own (#314): the model a unit borrows its clips from, and the
///     notice when that model's skeleton is not the hull's.
/// </summary>
public sealed class AnimationSectionBuilderTest
{
    private static EffectiveObject Unit(params (string Tag, string Value)[] tags)
    {
        var tagSource = new FakeVariantTagSource().With("Unit",
            tags.Select(t => new VariantTag(t.Tag, t.Value, $"<{t.Tag}>{t.Value}</{t.Tag}>", 0)).ToArray());
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("Unit",
            [
                new GameSymbol("Unit", GameSymbolKind.XmlObject, "GameObjectType",
                    new FileOrigin("file:///unit.xml", 0, 0), null, null)
            ])
        };
        return new EffectiveObjectResolver(index, new SchemaProviderBuilder().Build(), tagSource).Resolve("Unit");
    }

    private static GameIndex Bones(params (string Model, string[] Bones)[] models)
    {
        return GameIndex.Empty with
        {
            ModelBones = models.ToImmutableDictionary(m => ModelBoneKey.From(m.Model),
                m => m.Bones.ToImmutableArray(), StringComparer.OrdinalIgnoreCase)
        };
    }

    [Fact]
    public void AnimationsFor_IsEmpty_WhenTheIndexKnowsNoClips()
    {
        Assert.Empty(AnimationSectionBuilder.AnimationsFor(GameIndex.Empty, "RV_NEBULONB.ALO"));
    }

    [Fact]
    public void ResolveAnimationOverride_IsNull_WhenTheUnitDeclaresNone()
    {
        var problems = new List<PreviewProblem>();

        var model = new AnimationSectionBuilder(TestCatalogues.English)
            .ResolveAnimationOverride(GameIndex.Empty, Unit(), "hull.alo", problems);

        Assert.Null(model);
        Assert.Empty(problems);
    }

    [Fact]
    public void ResolveAnimationOverride_TakesTheLandTagFirst_AndNoticesADifferentSkeleton()
    {
        var problems = new List<PreviewProblem>();
        var index = Bones(("hull.alo", ["ROOT", "Spine"]), ("borrowed.alo", ["ROOT", "Spine", "Tail"]));

        var model = new AnimationSectionBuilder(TestCatalogues.English).ResolveAnimationOverride(index,
            Unit(("Space_Model_Anim_Override_Name", "space.alo"), ("Land_Model_Anim_Override_Name", "borrowed.alo")),
            "hull.alo", problems);

        Assert.Equal("borrowed.alo", model);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticIds.PreviewAnimationSkeletonMismatch, problem.DiagnosticId);
        Assert.Equal("info", problem.Severity);
    }

    [Fact]
    public void ResolveAnimationOverride_SaysNothing_WhenTheSkeletonsMatch_OrEitherIsUnknown()
    {
        var problems = new List<PreviewProblem>();
        var same = Bones(("hull.alo", ["ROOT", "Spine"]), ("borrowed.alo", ["root", "spine"]));
        var builder = new AnimationSectionBuilder(TestCatalogues.English);
        var unit = Unit(("Land_Model_Anim_Override_Name", "borrowed.alo"));

        Assert.Equal("borrowed.alo", builder.ResolveAnimationOverride(same, unit, "hull.alo", problems));
        Assert.Equal("borrowed.alo", builder.ResolveAnimationOverride(GameIndex.Empty, unit, "hull.alo", problems));
        Assert.Equal("borrowed.alo", builder.ResolveAnimationOverride(same, unit, null, problems));
        Assert.Empty(problems);
    }
}