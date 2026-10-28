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
///     The damage section on its own (#314): land damage stages and bands, the spin-away, the target
///     defence the attacker panel shoots at, and the death clones with their notice when undefined.
/// </summary>
public sealed class DamageSectionBuilderTest
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

    private static (GameIndex Index, EffectiveObjectResolver Resolver) Setup(FakeVariantTagSource tags,
        params GameSymbol[] symbols)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };
        return (index, new EffectiveObjectResolver(index, new SchemaProviderBuilder().Build(), tags));
    }

    private static DamageSectionBuilder Builder(FakeVariantTagSource tags)
    {
        return new DamageSectionBuilder(tags, new FakeAssets(), TestCatalogues.English);
    }

    [Fact]
    public void DamageStages_AreTheDistinctNonNegativeStages_Sorted()
    {
        var tags = new FakeVariantTagSource().With("Unit", Tag("Land_Damage_Alternates", "2, 1, 2, x, -1, 0"));
        var (_, resolver) = Setup(tags, Sym("Unit", "GameObjectType"));

        Assert.Equal([0, 1, 2], DamageSectionBuilder.DamageStages(resolver.Resolve("Unit")));
    }

    [Fact]
    public void DamageTable_PairsStagesWithThresholds_AndIsEmptyWhenTheCountsDiffer()
    {
        var tags = new FakeVariantTagSource()
            .With("Paired", Tag("Land_Damage_Alternates", "1, 2"), Tag("Land_Damage_Thresholds", "0.66, 0.33"))
            .With("Odd", Tag("Land_Damage_Alternates", "1, 2"), Tag("Land_Damage_Thresholds", "0.5"));
        var (_, resolver) = Setup(tags, Sym("Paired", "GameObjectType"), Sym("Odd", "GameObjectType"));

        Assert.Equal([new PreviewDamageBand(0.66f, 1), new PreviewDamageBand(0.33f, 2)],
            DamageSectionBuilder.DamageTable(resolver.Resolve("Paired")));
        Assert.Empty(DamageSectionBuilder.DamageTable(resolver.Resolve("Odd")));
    }

    [Fact]
    public void DiesWithHardpoints_IsOn_UnlessTheObjectDeniesIt()
    {
        var tags = new FakeVariantTagSource()
            .With("Silent", Tag("Max_Speed", "4"))
            .With("Denies", Tag("Should_Be_Destroyed_When_All_Hardpoints_Destroyed", "No"))
            .With("Says", Tag("Should_Be_Destroyed_When_All_Hardpoints_Destroyed", "Yes"));
        var (_, resolver) = Setup(tags, Sym("Silent", "GameObjectType"), Sym("Denies", "GameObjectType"),
            Sym("Says", "GameObjectType"));

        Assert.True(DamageSectionBuilder.DiesWithHardpoints(resolver.Resolve("Silent")));
        Assert.False(DamageSectionBuilder.DiesWithHardpoints(resolver.Resolve("Denies")));
        Assert.True(DamageSectionBuilder.DiesWithHardpoints(resolver.Resolve("Says")));
    }

    [Fact]
    public void SpinAway_ReadsTheFourTags_WithTheEnginesDefaults()
    {
        var tags = new FakeVariantTagSource()
            .With("Spins", Tag("Spin_Away_On_Death", "Yes"), Tag("Spin_Away_On_Death_Chance", "0.5"),
                Tag("Spin_Away_On_Death_Explosion", "Big_Boom"), Tag("Max_Speed", "4"))
            .With("Still", Tag("Max_Speed", "4"));
        var (_, resolver) = Setup(tags, Sym("Spins", "GameObjectType"), Sym("Still", "GameObjectType"));

        Assert.Equal(new PreviewSpinAway(2.0f, 0.5f, "Big_Boom", 4f),
            DamageSectionBuilder.SpinAway(resolver.Resolve("Spins")));
        Assert.Null(DamageSectionBuilder.SpinAway(resolver.Resolve("Still")));
    }

    [Fact]
    public void Defence_ReadsArmourShieldsAndHealth_AndSumsTheDestructibleHardpoints()
    {
        var tags = new FakeVariantTagSource().With("Ship",
            Tag("SpaceBehavior", "SELECTABLE, SHIELDED"), Tag("Armor_Type", " Armor_Frigate "),
            Tag("Shield_Armor_Type", "Shield_Frigate"), Tag("Shield_Points", "900"), Tag("Tactical_Health", "1800"),
            Tag("Energy_Capacity", "500"), Tag("Should_Be_Destroyed_When_All_Hardpoints_Destroyed", "No"));
        var (index, resolver) = Setup(tags, Sym("Ship", "GameObjectType"));
        PreviewHardpoint[] hardpoints =
        [
            new("HP_A", null, "T", "B", true, true, 100f, null, null, null, null, null, null, true, null, null, null),
            new("HP_B", null, "T", "B", false, true, 50f, null, null, null, null, null, null, true, null, null, null),
            new("HP_C", null, "T", "B", true, true, 25f, null, null, null, null, null, null, true, null, null, null)
        ];

        var defence = Builder(tags).Defence(index, resolver.Resolve("Ship"), hardpoints);

        Assert.True(defence.IsShielded);
        Assert.Equal("Armor_Frigate", defence.ArmorType);
        Assert.Equal("Shield_Frigate", defence.ShieldArmorType);
        Assert.Equal(900f, defence.ShieldPoints);
        Assert.Equal(1800f, defence.TacticalHealth);
        Assert.Equal(500f, defence.EnergyCapacity);
        Assert.Equal(125f, defence.HardpointHealthTotal);
        Assert.False(defence.DiesWithHardpoints);
        // No GameConstants in the tree: the engine's fallback, which both shipped corpora also write.
        Assert.Equal(0.2f, defence.HullVsHardpointsConstraint);
    }

    [Fact]
    public void DeathClones_ResolveTheRow_WithItsModelIdleAndScale()
    {
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Death_Clone", "Damage_Normal, Ship_Death_Clone"),
                Tag("Should_Death_Clone_Play_Idle", "Yes"))
            .With("Ship_Death_Clone", Tag("Space_Model_Name", "ship_death.alo"), Tag("Scale_Factor", "2"));
        var (index, resolver) = Setup(tags, Sym("Ship", "GameObjectType"), Sym("Ship_Death_Clone", "GameObjectType"));
        var problems = new List<PreviewProblem>();

        var clone = Assert.Single(Builder(tags).DeathClones(index, resolver, resolver.Resolve("Ship"), problems));

        Assert.Equal("Damage_Normal", clone.DamageType);
        Assert.Equal("Ship_Death_Clone", clone.ObjectId);
        Assert.Equal("ship_death.alo", clone.ModelFile);
        Assert.True(clone.PlaysIdle);
        Assert.Equal(2f, clone.ScaleFactor);
        Assert.Empty(problems);
    }

    [Fact]
    public void DeathClones_AnUndefinedClone_IsListedWithoutAModel_AndNoticed()
    {
        var tags = new FakeVariantTagSource().With("Ship", Tag("Death_Clone", ", Ship_Ghost_Clone"));
        var (index, resolver) = Setup(tags, Sym("Ship", "GameObjectType"));
        var problems = new List<PreviewProblem>();

        var clone = Assert.Single(Builder(tags).DeathClones(index, resolver, resolver.Resolve("Ship"), problems));

        Assert.Null(clone.DamageType);
        Assert.Equal("Ship_Ghost_Clone", clone.ObjectId);
        Assert.Null(clone.ModelFile);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticIds.PreviewDeathCloneNotDefined, problem.DiagnosticId);
        Assert.Contains("Ship_Ghost_Clone", problem.Message);
    }
}