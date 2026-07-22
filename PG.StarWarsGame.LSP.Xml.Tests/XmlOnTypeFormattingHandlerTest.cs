// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlOnTypeFormattingHandlerTest
{
    private const string TestUri = "file:///test.xml";

    private static DocumentOnTypeFormattingParams At(int line, int character, string ch = ">")
    {
        return new DocumentOnTypeFormattingParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(TestUri) },
            Position = new Position(line, character),
            Character = ch,
            Options = new FormattingOptions()
        };
    }

    private static XmlOnTypeFormattingHandler Build(string text, ILspConfigurationProvider? config = null,
        IEaWXmlContext? eaWContext = null)
    {
        var host = new FakeHost(TestUri, text);
        return new XmlOnTypeFormattingHandler(TestParseCache.For(host), eaWContext ?? new AllowAllEaWContext(),
            new FileHelper(new MockFileSystem()), config ?? new FakeLspConfigurationProvider());
    }

    // ── feature flag ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_AutoCloseFlagOff_ReturnsNull()
    {
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Xml = new XmlFeatureFlags { AutoCloseTag = false } });
        var handler = Build("<Root>\n<Foo>\n</Root>", config);

        var result = await handler.Handle(At(1, 5), CancellationToken.None);

        Assert.Null(result);
    }

    // ── happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_GreaterThanAfterOpenTag_InsertsClosingTagAtCursor()
    {
        // "<Root>\n<Foo>\n</Root>" - user just typed the '>' of <Foo>. Cursor at end of line 1.
        var handler = Build("<Root>\n<Foo>\n</Root>");

        var result = await handler.Handle(At(1, 5), CancellationToken.None);

        Assert.NotNull(result);
        var edit = Assert.Single(result!);
        Assert.Equal("</Foo>", edit.NewText);
        Assert.Equal(1, edit.Range.Start.Line);
        Assert.Equal(5, edit.Range.Start.Character);
        Assert.Equal(1, edit.Range.End.Line);
        Assert.Equal(5, edit.Range.End.Character);
    }

    [Fact]
    public async Task Handle_TagWithAttributes_InsertsBareCloseTag()
    {
        var handler = Build("<Root>\n<Foo Bar=\"x\">\n</Root>");

        // line 1: <Foo Bar="x">  → '>' at col 12, cursor at col 13
        var result = await handler.Handle(At(1, 13), CancellationToken.None);

        Assert.NotNull(result);
        var edit = Assert.Single(result!);
        Assert.Equal("</Foo>", edit.NewText);
    }

    [Fact]
    public async Task Handle_PreservesOriginalTagNameCase()
    {
        // HtmlAgilityPack lowercases node names; the inserted close tag must keep the source case.
        var handler = Build("<Root>\n<Max_Speed>\n</Root>");

        var result = await handler.Handle(At(1, 11), CancellationToken.None);

        Assert.NotNull(result);
        var edit = Assert.Single(result!);
        Assert.Equal("</Max_Speed>", edit.NewText);
    }

    // ── rejected contexts ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SelfClosingTag_ReturnsNull()
    {
        var handler = Build("<Root>\n<Foo/>\n</Root>");

        // line 1: <Foo/> → '>' at col 5, cursor at col 6
        var result = await handler.Handle(At(1, 6), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_ClosingTag_ReturnsNull()
    {
        // '>' completing a closing tag </Foo> must not trigger another close.
        var handler = Build("<Foo>\n</Foo>");

        // line 1: </Foo> → '>' at col 5, cursor at col 6
        var result = await handler.Handle(At(1, 6), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_AlreadyClosedTag_ReturnsNull()
    {
        // Re-typing the '>' of an element that already has its </Foo> must not duplicate it.
        var handler = Build("<Root>\n<Foo></Foo>\n</Root>");

        var result = await handler.Handle(At(1, 5), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_CharacterNotGreaterThan_ReturnsNull()
    {
        var handler = Build("<Root>\n<Foo>\n</Root>");

        var result = await handler.Handle(At(1, 5, "<"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_CursorNotAfterGreaterThan_ReturnsNull()
    {
        // Cursor sits on the tag name, not right after a '>'.
        var handler = Build("<Root>\n<Foo>\n</Root>");

        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        Assert.Null(result);
    }

    // ── non-EaW file ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonEaWFile_ReturnsNull()
    {
        var handler = Build("<Root>\n<Foo>\n</Root>", eaWContext: new DenyAllEaWContext());

        var result = await handler.Handle(At(1, 5), CancellationToken.None);

        Assert.Null(result);
    }

    // ── fake host ─────────────────────────────────────────────────────────────

    private sealed class FakeHost(string uri, string text) : IGameWorkspaceHost
    {
        public IEnumerable<TrackedDocument> All => [new(uri, text, 1)];

        public void AddOrUpdate(string u, string t, int v, bool publishDiagnostics = true)
        {
        }

        public void Remove(string u)
        {
        }

        public bool TryGet(string u, out TrackedDocument doc)
        {
            if (string.Equals(u, uri, StringComparison.OrdinalIgnoreCase))
            {
                doc = new TrackedDocument(uri, text, 1);
                return true;
            }

            doc = default!;
            return false;
        }
    }
}
