// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Lua;
using PG.StarWarsGame.LSP.Xml;

namespace PG.StarWarsGame.LSP.Server.Tests;

public class GameHoverHandlerTest
{
    [Fact]
    public async Task Handle_LuaUri_RoutesToLuaProvider()
    {
        var lua = new FakeLuaProvider(new Hover());
        var xml = new FakeXmlProvider(null);
        var handler = new GameHoverHandler(xml, lua);

        await handler.Handle(RequestFor("file:///script.lua"), CancellationToken.None);

        Assert.True(lua.WasCalled);
        Assert.False(xml.WasCalled);
    }

    [Fact]
    public async Task Handle_XmlUri_RoutesToXmlProvider()
    {
        var lua = new FakeLuaProvider(null);
        var xml = new FakeXmlProvider(new Hover());
        var handler = new GameHoverHandler(xml, lua);

        await handler.Handle(RequestFor("file:///data.xml"), CancellationToken.None);

        Assert.True(xml.WasCalled);
        Assert.False(lua.WasCalled);
    }

    [Fact]
    public async Task Handle_LuaUri_ReturnsLuaResult()
    {
        var expected = MakeHover("lua result");
        var handler = new GameHoverHandler(new FakeXmlProvider(null), new FakeLuaProvider(expected));

        var result = await handler.Handle(RequestFor("file:///script.lua"), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Handle_XmlUri_ReturnsXmlResult()
    {
        var expected = MakeHover("xml result");
        var handler = new GameHoverHandler(new FakeXmlProvider(expected), new FakeLuaProvider(null));

        var result = await handler.Handle(RequestFor("file:///data.xml"), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Handle_NonLuaUri_RoutesToXmlProvider()
    {
        var lua = new FakeLuaProvider(null);
        var xml = new FakeXmlProvider(null);
        var handler = new GameHoverHandler(xml, lua);

        await handler.Handle(RequestFor("file:///notes.txt"), CancellationToken.None);

        Assert.True(xml.WasCalled);
        Assert.False(lua.WasCalled);
    }

    private static HoverParams RequestFor(string uri) =>
        new() { TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) }, Position = new Position(0, 0) };

    private static Hover MakeHover(string text) =>
        new() { Contents = new MarkedStringsOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = text }) };

    private sealed class FakeXmlProvider(Hover? response) : IXmlHoverProvider
    {
        public bool WasCalled { get; private set; }

        public Task<Hover?> Handle(HoverParams request, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(response);
        }
    }

    private sealed class FakeLuaProvider(Hover? response) : ILuaHoverProvider
    {
        public bool WasCalled { get; private set; }

        public Task<Hover?> Handle(HoverParams request, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(response);
        }
    }
}
