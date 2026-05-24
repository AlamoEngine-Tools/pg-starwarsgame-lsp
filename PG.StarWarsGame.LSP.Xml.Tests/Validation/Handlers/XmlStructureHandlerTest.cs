// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class XmlStructureHandlerTest
{
    private static readonly XmlStructureHandler Sut = new();

    [Fact]
    public void Handle_emits_single_error_with_fact_reason_as_message()
    {
        var fact = new XmlStructureFact("file:///test.xml", 3, 0, 1,
            "The 'Bar' start tag does not match the end tag of 'Foo'.");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Equal("The 'Bar' start tag does not match the end tag of 'Foo'.", d.Message);
    }

    [Fact]
    public void Handle_passes_reason_verbatim()
    {
        var fact = new XmlStructureFact("file:///test.xml", 0, 0, 1, "Unexpected end of file.");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal("Unexpected end of file.", d.Message);
    }
}
