// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;
using Xunit;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class MapFileFormatHandlerTest
{
    private static readonly MapFileFormatHandler Sut = new();

    private static readonly XmlTagDefinition MapTag =
        XmlHandlerTestFixtures.MakeTag("Space_Tactical_Map", XmlValueType.NameReference,
            referenceKind: ReferenceKind.MapFile);

    [Theory]
    [InlineData("Maps/space_coruscant.ted")]
    [InlineData("Maps/space_coruscant.TED")]
    [InlineData("battle.ted")]
    public void Valid_map_extension_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(MapTag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("battle.xml")]
    [InlineData("battle.tga")]
    [InlineData("battle")]
    [InlineData("no_extension")]
    public void Invalid_map_extension_returns_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(MapTag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Non_map_tag_returns_no_diagnostics()
    {
        var otherTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(otherTag, "battle.xml"), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }
}
