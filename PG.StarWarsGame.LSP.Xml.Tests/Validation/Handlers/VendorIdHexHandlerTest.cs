// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class VendorIdHexHandlerTest
{
    private static readonly VendorIdHexHandler Sut = new();

    private static readonly XmlTagDefinition
        Tag = XmlHandlerTestFixtures.MakeTag("Vendor_Id", XmlValueType.VendorIdHex);

    [Theory]
    [InlineData("0x10de")]
    [InlineData("0x8086")]
    [InlineData("0xABCD1234")]
    public void Valid_vendor_hex_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("10de")]
    [InlineData("0x")]
    [InlineData("0xZZZZ")]
    [InlineData("")]
    public void Invalid_vendor_hex_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Non_vendor_hex_tag_returns_no_diagnostics()
    {
        var intTag = XmlHandlerTestFixtures.MakeTag("Count", XmlValueType.Int);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(intTag, "abc"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }
}