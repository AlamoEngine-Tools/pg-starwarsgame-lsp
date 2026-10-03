// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class TupleListHandlerTest
{
    private static readonly TupleListHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Music_Events", XmlValueType.TupleList);

    [Theory]
    [InlineData("Music_Battle_01, 3")]
    [InlineData("Music_Victory,1")]
    [InlineData("Music_Ambient, 2.5")]
    public void Valid_pair_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Music_Battle_01")]
    [InlineData(",3")]
    [InlineData("Music_Battle_01, 0")]
    [InlineData("Music_Battle_01, -1")]
    [InlineData("Music_Battle_01, not_a_number")]
    [InlineData("Music_Battle_01,")]
    public void Invalid_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Wrong_type_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, ""), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }
}