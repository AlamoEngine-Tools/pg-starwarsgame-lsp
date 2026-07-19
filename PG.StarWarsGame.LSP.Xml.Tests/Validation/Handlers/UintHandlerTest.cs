// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class UintHandlerTest
{
    private static readonly UintHandler Sut = new();

    private static readonly XmlTagDefinition UintTag =
        XmlHandlerTestFixtures.MakeTag("Population_Limit", XmlValueType.UInt);

    private static readonly XmlTagDefinition HardwareUIntTag =
        XmlHandlerTestFixtures.MakeTag("Hardware_Flags", XmlValueType.HardwareUInt);

    [Theory]
    [InlineData("0")]
    [InlineData("42")]
    [InlineData("2147483647")]
    [InlineData("4294967295")]
    public void Valid_uint_values_return_no_diagnostics(string value)
    {
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(UintTag, value), XmlHandlerTestFixtures.EmptyCtx));
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(HardwareUIntTag, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public void Non_numeric_returns_error(string value)
    {
        var r1 = Sut.Handle(XmlHandlerTestFixtures.MakeFact(UintTag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Equal(XmlDiagnosticSeverity.Error, Assert.Single(r1).Severity);

        var r2 = Sut.Handle(XmlHandlerTestFixtures.MakeFact(HardwareUIntTag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Equal(XmlDiagnosticSeverity.Error, Assert.Single(r2).Severity);
    }

    [Theory]
    [InlineData("-1", "0")]
    [InlineData("1.5", "1")]
    [InlineData("-0.7", "0")]
    public void Float_or_negative_returns_warning_with_clamped_fix(string value, string expectedFix)
    {
        foreach (var tag in new[] { UintTag, HardwareUIntTag })
        {
            var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, value), XmlHandlerTestFixtures.EmptyCtx)
                .ToList();
            var d = Assert.Single(results);
            Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
            Assert.Equal(expectedFix, d.SuggestedFix);
        }
    }

    [Fact]
    public void Non_uint_tag_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, "abc"), XmlHandlerTestFixtures.EmptyCtx));
    }
}