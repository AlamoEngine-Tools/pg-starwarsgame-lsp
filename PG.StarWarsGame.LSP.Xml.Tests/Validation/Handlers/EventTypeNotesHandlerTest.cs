// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class EventTypeNotesHandlerTest
{
    private static readonly EventTypeNotesHandler Sut = new();

    [Fact]
    public void Event_with_matching_locale_note_emits_hint()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), GameIndex.Empty, "file:///test.xml", "en");
        var def = new EnumValueDefinition
        {
            Name = "MY_EVENT",
            Notes = new Dictionary<string, string> { ["en"] = "Never used in vanilla." }
        };
        var fact = new StoryEventFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, def);
        var results = Sut.Handle(fact, ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Hint, d.Severity);
        Assert.Contains("Never used in vanilla.", d.Message);
    }

    [Fact]
    public void Event_with_different_locale_emits_no_diagnostics()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), GameIndex.Empty, "file:///test.xml", "de");
        var def = new EnumValueDefinition
        {
            Name = "MY_EVENT",
            Notes = new Dictionary<string, string> { ["en"] = "Some note." }
        };
        var fact = new StoryEventFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, def);
        var results = Sut.Handle(fact, ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Null_def_emits_no_diagnostics()
    {
        var fact = new StoryEventFact("file:///test.xml", 1, 0, 0, "UNKNOWN", false, null);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }
}