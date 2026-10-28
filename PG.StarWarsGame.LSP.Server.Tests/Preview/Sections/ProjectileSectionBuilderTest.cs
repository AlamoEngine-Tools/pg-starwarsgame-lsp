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
///     The projectile section on its own (#314): the projectiles a list of ids names, resolved through
///     the variant chain, and the catalogue of every projectile the tree declares.
/// </summary>
public sealed class ProjectileSectionBuilderTest
{
    private static GameSymbol Sym(string id, string typeName, string? variantBaseId = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin($"file:///{id}.xml", 0, 0), null, variantBaseId);
    }

    private static GameIndex Index(params GameSymbol[] symbols)
    {
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    [Fact]
    public void ProjectilesFor_ResolvesEachIdOnce_AndNoticesTheUndefined()
    {
        var index = Index(Sym("Proj_Red", "GameObjectType", "Proj_Generic"), Sym("Proj_Generic", "GameObjectType"));
        var tags = new FakeVariantTagSource()
            .With("Proj_Generic", Tag("Max_Speed", "11"), Tag("Projectile_Does_Shield_Damage", "Yes"))
            .With("Proj_Red", Tag("Variant_Of_Existing_Type", "Proj_Generic"), Tag("Projectile_Damage", "5"),
                Tag("Space_Model_Name", "W_LASER_SMALL.ALO"));
        var resolver = new EffectiveObjectResolver(index, new SchemaProviderBuilder().Build(), tags);
        var problems = new List<PreviewProblem>();

        var projectiles = ProjectileSectionBuilder.ProjectilesFor(resolver, ["Proj_Red", "proj_red", "Proj_Missing"],
            problems, TestCatalogues.English);

        var red = Assert.Single(projectiles);
        Assert.Equal("Proj_Red", red.Id);
        Assert.Equal(5f, red.Damage);
        Assert.Equal(11f, red.Speed);
        Assert.True(red.DoesShieldDamage);
        Assert.Equal("W_LASER_SMALL.ALO", red.ModelFile);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticIds.PreviewProjectileNotDefined, problem.DiagnosticId);
        Assert.Contains("Proj_Missing", problem.Message);
    }

    [Fact]
    public void ProjectileCatalog_NamesEveryObjectWithAProjectileBehaviour_AndItsVariants_Sorted()
    {
        var index = Index(
            Sym("Proj_Zeta", "GameObjectType"),
            Sym("Proj_Alpha_Variant", "GameObjectType", "Proj_Zeta"),
            Sym("Xwing", "GameObjectType"));
        var tags = new FakeVariantTagSource()
            .With("Proj_Zeta", Tag("Behavior", "PROJECTILE, HIDE_WHEN_FOGGED"))
            .With("Proj_Alpha_Variant", Tag("Variant_Of_Existing_Type", "Proj_Zeta"))
            .With("Xwing", Tag("SpaceBehavior", "SELECTABLE, WEAPON"));
        var builder = new ProjectileSectionBuilder(tags, TestCatalogues.English);

        var catalog = builder.ProjectileCatalog(index);

        Assert.Equal(["Proj_Alpha_Variant", "Proj_Zeta"], catalog);
        Assert.Same(catalog, builder.ProjectileCatalog(index));
    }
}