// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     <c>Projectile_Types_Targeted</c> is the only tag with this value type, and nothing checked it.
/// </summary>
/// <remarks>
///     Engine type code <c>0x51</c> (81) at <c>017f5020</c>, on <c>Laser_Defense_Ability</c>: the
///     list of projectile categories a point-defence laser will shoot down. A typo in it does not
///     fail the load - the category simply never matches, so the defence silently ignores that
///     projectile, which is the hardest kind of mistake to see in-game.
/// </remarks>
public sealed class ProjectileCategoryListHandlerTest
{
    private static readonly ProjectileCategoryListHandler Sut = new();

    private static EnumDefinition Categories()
    {
        return new EnumDefinition
        {
            Name = "ProjectileCategory",
            Kind = EnumKind.SchemaFixed,
            Values =
            [
                new EnumValueDefinition { Name = "MISSILE" },
                new EnumValueDefinition { Name = "ROCKET" },
                new EnumValueDefinition { Name = "MPTL_Rocket" },
                new EnumValueDefinition { Name = "Laser" }
            ]
        };
    }

    private static XmlTagValueFact Fact(string value, EnumDefinition? enumDef = null)
    {
        var tag = XmlHandlerTestFixtures.MakeTag(
            "Projectile_Types_Targeted", XmlValueType.ProjectileCategoryList,
            enumDef: enumDef ?? Categories());
        return XmlHandlerTestFixtures.MakeFact(tag, value);
    }

    // The one value the shipped corpus writes, on both MPTL units.
    [Fact]
    public void The_shipped_value_is_accepted()
    {
        Assert.Empty(Sut.Handle(Fact("MISSILE, ROCKET, MPTL_ROCKET"), XmlHandlerTestFixtures.EmptyCtx));
    }

    // Vanilla writes MPTL_ROCKET where the enum says MPTL_Rocket, so the engine plainly does not
    // care about case and neither can this.
    [Theory]
    [InlineData("missile")]
    [InlineData("MPTL_ROCKET")]
    [InlineData("mptl_rocket")]
    public void Casing_does_not_matter(string value)
    {
        Assert.Empty(Sut.Handle(Fact(value), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Unknown_category_is_an_error()
    {
        var result = Assert.Single(
            Sut.Handle(Fact("MISSILE, ROKCET"), XmlHandlerTestFixtures.EmptyCtx).ToList());

        Assert.Equal(XmlDiagnosticSeverity.Error, result.Severity);
        Assert.Contains("ROKCET", result.Message);
    }

    // Items are conventionally one per line, so a token's newlines must be trimmed off first.
    [Fact]
    public void Multiline_list_is_accepted()
    {
        const string value = "\n\t\tMISSILE,\n\t\tROCKET\n\t";

        Assert.Empty(Sut.Handle(Fact(value), XmlHandlerTestFixtures.EmptyCtx));
    }

    // A tag with no enum wired is left alone rather than measured against the wrong set - the same
    // rule VictoryConditionListHandler follows.
    [Fact]
    public void No_enum_wired_means_no_check()
    {
        var tag = XmlHandlerTestFixtures.MakeTag(
            "Projectile_Types_Targeted", XmlValueType.ProjectileCategoryList);

        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "whatever"),
            XmlHandlerTestFixtures.EmptyCtx));
    }
}
