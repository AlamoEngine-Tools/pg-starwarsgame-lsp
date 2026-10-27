// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Features;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>
///     The hover handler knows no file extension (#312): it hands the request to the first provider
///     that says it handles the document, and answers nothing when none does.
/// </summary>
public class GameHoverHandlerTest
{
    private static GameHoverHandler Build(params IHoverProvider[] providers)
    {
        return new GameHoverHandler(providers, new FileHelper(new MockFileSystem()),
            new NullLogger<GameHoverHandler>());
    }

    [Fact]
    public async Task Handle_ReachesTheProviderThatHandlesTheDocument()
    {
        var lua = new FakeProvider(".lua", null);
        var xml = new FakeProvider(".xml", new Hover());
        var handler = Build(xml, lua);

        await handler.Handle(RequestFor("file:///data.xml"), CancellationToken.None);

        Assert.True(xml.WasCalled);
        Assert.False(lua.WasCalled);
    }

    [Fact]
    public async Task Handle_UppercaseExtension_StillReachesItsProvider()
    {
        var lua = new FakeProvider(".lua", null);
        var xml = new FakeProvider(".xml", new Hover());
        var handler = Build(xml, lua);

        await handler.Handle(RequestFor("FILE:///DATA.XML"), CancellationToken.None);

        Assert.True(xml.WasCalled);
        Assert.False(lua.WasCalled);
    }

    [Fact]
    public async Task Handle_ReturnsTheProvidersAnswer()
    {
        var expected = MakeHover("lua result");
        var handler = Build(new FakeProvider(".xml", null), new FakeProvider(".lua", expected));

        var result = await handler.Handle(RequestFor("file:///script.lua"), CancellationToken.None);

        Assert.Same(expected, result);
    }

    /// <summary>A text file reaches hover the moment a provider handles it; the handler needs no edit.</summary>
    [Fact]
    public async Task Handle_ATextFile_ReachesTheProviderThatHandlesIt()
    {
        var xml = new FakeProvider(".xml", null);
        var txt = new FakeProvider(".txt", new Hover());
        var handler = Build(xml, txt);

        await handler.Handle(RequestFor("file:///dialog.txt"), CancellationToken.None);

        Assert.True(txt.WasCalled);
        Assert.False(xml.WasCalled);
    }

    [Fact]
    public async Task Handle_NoProviderHandlesTheDocument_AnswersNothing()
    {
        var lua = new FakeProvider(".lua", new Hover());
        var xml = new FakeProvider(".xml", new Hover());
        var handler = Build(xml, lua);

        var result = await handler.Handle(RequestFor("file:///notes.txt"), CancellationToken.None);

        Assert.Null(result);
        Assert.False(xml.WasCalled);
        Assert.False(lua.WasCalled);
    }

    [Fact]
    public async Task Handle_TwoProvidersHandleTheDocument_TheFirstRegisteredAnswers()
    {
        var first = new FakeProvider(".xml", MakeHover("first"));
        var second = new FakeProvider(".xml", MakeHover("second"));

        var result = await Build(first, second).Handle(RequestFor("file:///data.xml"), CancellationToken.None);

        Assert.Same(first.Response, result);
        Assert.False(second.WasCalled);
    }

    private static HoverParams RequestFor(string uri)
    {
        return new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) }, Position = new Position(0, 0)
        };
    }

    private static Hover MakeHover(string text)
    {
        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = text })
        };
    }

    private sealed class FakeProvider(string extension, Hover? response) : IHoverProvider
    {
        public bool WasCalled { get; private set; }
        public Hover? Response => response;

        public bool Handles(string documentUri)
        {
            return documentUri.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        public Task<Hover?> HoverAsync(HoverParams request, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(response);
        }
    }
}