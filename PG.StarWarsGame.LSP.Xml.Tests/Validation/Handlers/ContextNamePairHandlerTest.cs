// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class ContextNamePairHandlerTest
{
    private static readonly ContextNamePairHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Music_Event_List_Ambient", XmlValueType.TupleList);

    [Theory]
    [InlineData("Space, Space_Map_Rebel_Ambient_Music_Event")]
    [InlineData("Temperate,Temperate_Land_Rebel_Ambient_Music_Event")]
    [InlineData(" Arctic , Ice_Land_Music_Event ")]
    public void Valid_context_name_pair_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Space")]
    [InlineData(",Event_Name")]
    [InlineData("Space,")]
    [InlineData("  ,  ")]
    public void Invalid_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    // ── Music event name validation ───────────────────────────────────────────

    private static GameIndex IndexWithSymbol(string id)
    {
        var sym = new GameSymbol(id, GameSymbolKind.XmlObject, "MusicEvent", new UnknownOrigin("test"), null);
        var defs = ImmutableDictionary.Create<string, ImmutableArray<GameSymbol>>(StringComparer.OrdinalIgnoreCase)
            .Add(id, ImmutableArray.Create(sym));
        return new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty,
            defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    [Fact]
    public void Known_music_event_returns_no_diagnostics()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithSymbol("Space_Ambient_Music"),
            "file:///test.xml", "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Space, Space_Ambient_Music"), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Unknown_music_event_returns_error()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithSymbol("Space_Ambient_Music"),
            "file:///test.xml", "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Space, Missing_Event"), ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Missing_Event", d.Message);
    }

    [Fact]
    public void Empty_index_skips_name_validation()
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Space, Missing_Event"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Name_lookup_is_case_insensitive()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithSymbol("Space_Ambient_Music"),
            "file:///test.xml", "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Space, space_ambient_music"), ctx).ToList();
        Assert.Empty(results);
    }
}
