// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class XmlNotesHandlerTest
{
    private static readonly XmlNotesHandler Sut = new();

    private static XmlTagDefinition TagWithNote(string locale, string note)
    {
        return new XmlTagDefinition
        {
            Tag = "Speed",
            ValueType = XmlValueType.Float,
            Notes = new Dictionary<string, string> { [locale] = note }
        };
    }

    [Fact]
    public void Tag_with_matching_locale_note_emits_hint()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), GameIndex.Empty, "file:///test.xml", "en");
        var fact = new XmlNotesFact("file:///test.xml", 1, 0, 0, TagWithNote("en", "Max speed of the unit"));
        var results = Sut.Handle(fact, ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Hint, d.Severity);
        Assert.Equal("Max speed of the unit", d.Message);
    }

    [Fact]
    public void Tag_with_different_locale_returns_no_diagnostics()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), GameIndex.Empty, "file:///test.xml", "de");
        var fact = new XmlNotesFact("file:///test.xml", 1, 0, 0, TagWithNote("en", "Max speed of the unit"));
        var results = Sut.Handle(fact, ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Tag_with_no_notes_returns_no_diagnostics()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), GameIndex.Empty, "file:///test.xml", "en");
        var tag = new XmlTagDefinition { Tag = "Speed", ValueType = XmlValueType.Float };
        var fact = new XmlNotesFact("file:///test.xml", 1, 0, 0, tag);
        var results = Sut.Handle(fact, ctx).ToList();
        Assert.Empty(results);
    }
}