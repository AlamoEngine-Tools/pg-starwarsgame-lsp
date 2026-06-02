// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;
using Xunit;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class ModelFileFormatHandlerTest
{
    private static readonly ModelFileFormatHandler Sut = new();

    private static readonly XmlTagDefinition ModelTag =
        XmlHandlerTestFixtures.MakeTag("Space_Model_Name", XmlValueType.NameReference,
            referenceKind: ReferenceKind.ModelFile);

    [Theory]
    [InlineData("ART/Units/SpaceUnit.alo")]
    [InlineData("ART/Units/SpaceUnit.ALO")]
    [InlineData("unit.alo")]
    public void Valid_model_extension_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(ModelTag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("unit.tga")]
    [InlineData("unit.dds")]
    [InlineData("unit.obj")]
    [InlineData("unit")]
    [InlineData("no_extension")]
    public void Invalid_model_extension_returns_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(ModelTag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Non_model_tag_returns_no_diagnostics()
    {
        var otherTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(otherTag, "unit.obj"), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }
}
