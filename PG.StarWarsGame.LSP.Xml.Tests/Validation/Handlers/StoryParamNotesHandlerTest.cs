// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class StoryParamNotesHandlerTest
{
    private static readonly StoryParamNotesHandler Sut = new();

    private static ParamDefinition DefWithNote(string locale, string note)
    {
        return new ParamDefinition
        {
            Position = 0,
            ValueType = XmlValueType.Int,
            Optional = true,
            Notes = new Dictionary<string, string> { [locale] = note }
        };
    }

    [Fact]
    public void Param_with_matching_locale_note_emits_hint()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), GameIndex.Empty, "file:///test.xml", "en");
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0,
            DefWithNote("en", "Param note here."), "42");
        var results = Sut.Handle(fact, ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Hint, d.Severity);
        Assert.Equal("Param note here.", d.Message);
    }

    [Fact]
    public void Param_with_different_locale_emits_no_diagnostics()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), GameIndex.Empty, "file:///test.xml", "de");
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0,
            DefWithNote("en", "Param note here."), "42");
        Assert.Empty(Sut.Handle(fact, ctx));
    }

    [Fact]
    public void Empty_value_emits_no_notes_hint()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), GameIndex.Empty, "file:///test.xml", "en");
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0,
            DefWithNote("en", "Param note here."), "");
        Assert.Empty(Sut.Handle(fact, ctx));
    }
}