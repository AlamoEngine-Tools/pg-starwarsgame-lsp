// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class DamageNonzeroHandlerTest
{
    private static readonly DamageNonzeroHandler Sut = new();
    private static readonly XmlTagDefinition DamageTag = XmlHandlerTestFixtures.MakeTag("Damage", XmlValueType.Float);

    [Fact]
    public void ValidationId_is_damage_nonzero()
    {
        Assert.Equal("damage-nonzero", Sut.ValidationId);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("0.0f")]
    [InlineData("-1")]
    [InlineData("-0.001")]
    [InlineData("-100.0")]
    public void Non_positive_value_emits_warning(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(DamageTag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("1.0")]
    [InlineData("100")]
    [InlineData("1500.5f")]
    public void Positive_value_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(DamageTag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("not-a-number")]
    public void Non_parseable_value_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(DamageTag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();

        Assert.Empty(results);
    }
}
