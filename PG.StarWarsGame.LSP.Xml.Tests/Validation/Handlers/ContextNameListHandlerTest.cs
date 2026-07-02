// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class ContextNameListHandlerTest
{
    private static readonly ContextNameListHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Land_Terrain_Model_Mapping", XmlValueType.TupleList);

    [Theory]
    [InlineData("Temperate, EI_TROOPER.ALO")]
    [InlineData("Temperate, EI_TROOPER.ALO, Urban, EI_TROOPER.ALO")]
    [InlineData("Temperate, EI_TROOPER.ALO, Arctic, EI_TROOPER_Snow.ALO,")]
    [InlineData("A, B, C, D, E, F,")]
    public void Valid_context_name_list_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Temperate")]
    [InlineData("Temperate, EI_TROOPER.ALO, Urban")]
    [InlineData(", EI_TROOPER.ALO")]
    [InlineData("Temperate,")]
    public void Invalid_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }
}
