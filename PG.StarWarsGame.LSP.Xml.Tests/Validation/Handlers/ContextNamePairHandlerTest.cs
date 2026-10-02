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

    // ── what the items are is not this handler's business ─────────────────────

    /// <summary>
    ///     The music event used to be looked up here, by name and against nothing in particular.
    ///     It is the second slot now, typed as a MusicEvent reference: the parser records it, and
    ///     the reference pipeline resolves it - with hover, go-to and rename besides.
    /// </summary>
    [Fact]
    public void AnUnknownMusicEvent_IsLeftToTheReferenceCheck()
    {
        var sym = new GameSymbol("Space_Ambient_Music", GameSymbolKind.XmlObject, "MusicEvent",
            new UnknownOrigin("test"), null);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty,
            ImmutableDictionary.Create<string, ImmutableArray<GameSymbol>>(StringComparer.OrdinalIgnoreCase)
                .Add(sym.Id, [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");

        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Space, Missing_Event"), ctx));
    }

    [Fact]
    public void ABadPair_IsDescribedInTheSlotsWords()
    {
        var slotted = Tag with
        {
            Slots =
            [
                new TupleSlotDefinition { Label = "Context" },
                new TupleSlotDefinition { Label = "Music event" }
            ]
        };

        var d = Assert.Single(Sut.Handle(XmlHandlerTestFixtures.MakeFact(slotted, "Space"),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Contains("`Context, Music event`", d.Message);
    }
}