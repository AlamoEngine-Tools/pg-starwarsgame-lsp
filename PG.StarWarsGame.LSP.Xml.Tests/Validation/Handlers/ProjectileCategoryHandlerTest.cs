// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class ProjectileCategoryHandlerTest
{
    private static readonly ProjectileCategoryHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Projectile_Category", XmlValueType.ProjectileCategory);

    private static XmlTagDefinition TagWithEnum(params string[] values)
    {
        var enumDef = new EnumDefinition
        {
            Name = "ProjectileCategory",
            Kind = EnumKind.SchemaFixed,
            Values = values.Select(v => new EnumValueDefinition { Name = v }).ToList()
        };
        return XmlHandlerTestFixtures.MakeTag("Projectile_Category", XmlValueType.ProjectileCategory, enumDef: enumDef);
    }

    [Theory]
    [InlineData("Laser")]
    [InlineData("Missile")]
    public void Non_empty_values_without_enum_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Non_projectile_category_tag_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, ""), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    // ── Schema enum validation ────────────────────────────────────────────────

    [Theory]
    [InlineData("Laser")]
    [InlineData("MISSILE")]
    [InlineData("laser")]
    public void Known_value_with_schema_enum_returns_no_diagnostics(string value)
    {
        var tag = TagWithEnum("Laser", "MISSILE", "BOMB");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Unknown_value_with_schema_enum_returns_error()
    {
        var tag = TagWithEnum("Laser", "MISSILE", "BOMB");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "GARBAGE"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("GARBAGE", d.Message);
    }

    [Fact]
    public void No_enum_on_tag_skips_value_validation()
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "ANYTHING_GOES"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }
}