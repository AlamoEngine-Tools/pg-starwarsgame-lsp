// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class TextureFileFormatHandlerTest
{
    private static readonly TextureFileFormatHandler Sut = new();

    private static readonly XmlTagDefinition TextureTag =
        XmlHandlerTestFixtures.MakeTag("GUI_Model_Name", XmlValueType.NameReference,
            referenceKind: ReferenceKind.TextureFile);

    [Theory]
    [InlineData("ART/Units/Foo.tga")]
    [InlineData("ART/Units/Foo.TGA")]
    [InlineData("ART/Units/Foo.dds")]
    [InlineData("ART/Units/Foo.DDS")]
    [InlineData("icon.tga")]
    [InlineData("icon.dds")]
    public void Valid_texture_extensions_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(TextureTag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("icon.png")]
    [InlineData("icon.bmp")]
    [InlineData("icon.jpg")]
    [InlineData("icon.alo")]
    [InlineData("icon")]
    [InlineData("no_extension")]
    public void Invalid_texture_extensions_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(TextureTag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Non_texture_tag_returns_no_diagnostics()
    {
        var otherTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(otherTag, "icon.png"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }
}