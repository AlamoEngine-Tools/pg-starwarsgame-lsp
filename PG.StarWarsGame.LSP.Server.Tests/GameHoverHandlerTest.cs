// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua;
using PG.StarWarsGame.LSP.Xml;
using System.IO.Abstractions.TestingHelpers;

namespace PG.StarWarsGame.LSP.Server.Tests;

public class GameHoverHandlerTest
{
    private static GameHoverHandler Build(IXmlHoverProvider xml, ILuaHoverProvider lua)
    {
        return new GameHoverHandler(xml, lua, new FileHelper(new MockFileSystem()),
            new NullLogger<GameHoverHandler>());
    }

    [Fact]
    public async Task Handle_UppercaseXmlExtension_RoutesToXmlProvider()
    {
        var lua = new FakeLuaProvider(null);
        var xml = new FakeXmlProvider(new Hover());
        var handler = Build(xml, lua);

        await handler.Handle(RequestFor("FILE:///DATA.XML"), CancellationToken.None);

        Assert.True(xml.WasCalled);
        Assert.False(lua.WasCalled);
    }

    [Fact]
    public async Task Handle_LuaUri_RoutesToLuaProvider()
    {
        var lua = new FakeLuaProvider(new Hover());
        var xml = new FakeXmlProvider(null);
        var handler = Build(xml, lua);

        await handler.Handle(RequestFor("file:///script.lua"), CancellationToken.None);

        Assert.True(lua.WasCalled);
        Assert.False(xml.WasCalled);
    }

    [Fact]
    public async Task Handle_XmlUri_RoutesToXmlProvider()
    {
        var lua = new FakeLuaProvider(null);
        var xml = new FakeXmlProvider(new Hover());
        var handler = Build(xml, lua);

        await handler.Handle(RequestFor("file:///data.xml"), CancellationToken.None);

        Assert.True(xml.WasCalled);
        Assert.False(lua.WasCalled);
    }

    [Fact]
    public async Task Handle_LuaUri_ReturnsLuaResult()
    {
        var expected = MakeHover("lua result");
        var handler = Build(new FakeXmlProvider(null), new FakeLuaProvider(expected));

        var result = await handler.Handle(RequestFor("file:///script.lua"), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Handle_XmlUri_ReturnsXmlResult()
    {
        var expected = MakeHover("xml result");
        var handler = Build(new FakeXmlProvider(expected), new FakeLuaProvider(null));

        var result = await handler.Handle(RequestFor("file:///data.xml"), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Handle_NonLuaUri_RoutesToXmlProvider()
    {
        var lua = new FakeLuaProvider(null);
        var xml = new FakeXmlProvider(null);
        var handler = Build(xml, lua);

        await handler.Handle(RequestFor("file:///notes.txt"), CancellationToken.None);

        Assert.False(xml.WasCalled);
        Assert.False(lua.WasCalled);
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