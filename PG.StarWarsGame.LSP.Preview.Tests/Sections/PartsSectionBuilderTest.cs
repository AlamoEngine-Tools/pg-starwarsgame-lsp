// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Preview.Sections;
using PG.StarWarsGame.LSP.Preview;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Preview.Tests.Sections;

/// <summary>
///     The parts section on its own (#314): the hardpoint ids a unit mounts, and what mounting one
///     adds - a part when it attaches a model, the hardpoint row, its weapon, and the problems.
/// </summary>
public sealed class PartsSectionBuilderTest
{
    private static GameSymbol Sym(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin($"file:///{id}.xml", 0, 0), null, null);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    private static (GameIndex Index, EffectiveObjectResolver Resolver, FakeVariantTagSource Tags) Setup(
        FakeVariantTagSource tags, params GameSymbol[] symbols)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase),
            ModelBones = GameIndex.Empty.ModelBones.Add(ModelBoneKey.From("hull.alo"), ["ROOT", "HP_F-L_BONE"])
        };
        return (index, new EffectiveObjectResolver(index, new SchemaProviderBuilder().Build(), tags), tags);
    }

    [Fact]
    public void HardpointIds_SplitTheListAsWritten()
    {
        var (_, resolver, _) = Setup(new FakeVariantTagSource().With("Ship", Tag("HardPoints", " HP_A, HP_B ,HP_C ")),
            Sym("Ship", "GameObjectType"));

        Assert.Equal(["HP_A", "HP_B", "HP_C"], PartsSectionBuilder.HardpointIds(resolver.Resolve("Ship")));
        Assert.Empty(PartsSectionBuilder.HardpointIds(resolver.Resolve("Nobody")));
    }

    [Fact]
    public void AddHardpoint_MountsTheModelAsAPart_AndReadsTheRow()
    {
        var tags = new FakeVariantTagSource().With("HP_Laser",
            Tag("Type", "HARD_POINT_WEAPON_LASER"), Tag("Attachment_Bone", "HP_F-L_BONE"),
            Tag("Model_To_Attach", "turret.alo"), Tag("Is_Destroyable", "Yes"), Tag("Is_Targetable", "Yes"),
            Tag("Health", "250"), Tag("Fire_Bone_A", "FP_00"), Tag("Collision_Mesh", "HP_F-L_COLL"));
        var (index, resolver, _) = Setup(tags, Sym("HP_Laser", "HardPoint"));
        var builder = new PartsSectionBuilder(new FakeAssets("turret.alo"), tags, TestCatalogues.English);
        var parts = new List<PreviewPart>();
        var hardpoints = new List<PreviewHardpoint>();
        var weapons = new List<PreviewWeapon>();
        var problems = new List<PreviewProblem>();

        builder.AddHardpoint(index, resolver, "hull.alo", "HP_Laser", true, parts, hardpoints, weapons, problems);

        var part = Assert.Single(parts);
        Assert.Equal(new PreviewPart("hp:HP_Laser", "turret.alo", "hull", "HP_F-L_BONE", PreviewPartOrigin.Hardpoint,
            "HP_Laser", true), part);
        var hardpoint = Assert.Single(hardpoints);
        Assert.Equal("HP_Laser", hardpoint.Id);
        Assert.Equal("hp:HP_Laser", hardpoint.PartId);
        Assert.Equal(250f, hardpoint.Health);
        Assert.True(hardpoint.IsTargetable);
        Assert.Equal("hardpoint:HP_Laser", Assert.Single(weapons).Id);
        Assert.Empty(problems);
    }

    [Fact]
    public void AddHardpoint_NotDefined_IsOneWarning_AndNothingElse()
    {
        var tags = new FakeVariantTagSource();
        var (index, resolver, _) = Setup(tags);
        var builder = new PartsSectionBuilder(new FakeAssets(), tags, TestCatalogues.English);
        var parts = new List<PreviewPart>();
        var hardpoints = new List<PreviewHardpoint>();
        var problems = new List<PreviewProblem>();

        builder.AddHardpoint(index, resolver, "hull.alo", "HP_Ghost", true, parts, hardpoints, [], problems);

        Assert.Empty(parts);
        Assert.Empty(hardpoints);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticIds.PreviewHardpointNotDefined, problem.DiagnosticId);
        Assert.Equal("HP_Ghost", problem.HardpointId);
    }

    [Fact]
    public void AddHardpoint_WithoutAnAttachmentBone_IsAnError_AndTheMissingModelAWarning()
    {
        var tags = new FakeVariantTagSource().With("HP_Loose",
            Tag("Type", "HARD_POINT_ENGINE"), Tag("Model_To_Attach", "missing.alo"), Tag("Is_Targetable", "No"));
        var (index, resolver, _) = Setup(tags, Sym("HP_Loose", "HardPoint"));
        var builder = new PartsSectionBuilder(new FakeAssets(), tags, TestCatalogues.English);
        var parts = new List<PreviewPart>();
        var problems = new List<PreviewProblem>();

        builder.AddHardpoint(index, resolver, "hull.alo", "HP_Loose", false, parts, [], [], problems);

        Assert.False(Assert.Single(parts).Resolved);
        Assert.Equal([DiagnosticIds.PreviewHardpointModelNotFound, DiagnosticIds.PreviewHardpointNoBone],
            problems.Select(p => p.DiagnosticId));
        Assert.Equal(["warning", "error"], problems.Select(p => p.Severity));
    }
}