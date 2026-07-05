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

public sealed class XmlLinkedEditingRangeHandlerTest
{
    private const string TestUri = "file:///test.xml";

    private static LinkedEditingRangeParams At(int line, int character)
    {
        return new LinkedEditingRangeParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(TestUri) },
            Position = new Position(line, character)
        };
    }

    private static XmlLinkedEditingRangeHandler Build(string text, ILspConfigurationProvider? config = null)
    {
        var host = new FakeHost(TestUri, text);
        return new XmlLinkedEditingRangeHandler(TestParseCache.For(host), new AllowAllEaWContext(),
            new FileHelper(new MockFileSystem()), config ?? new FakeLspConfigurationProvider());
    }

    // ── feature flag ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LinkedEditingFlagOff_ReturnsNull()
    {
        // Same arrange as Handle_CursorOnOpeningTagName — only the flag differs.
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Xml = new XmlFeatureFlags { LinkedEditing = false } });
        var handler = Build("<Foo>\nbar\n</Foo>", config);

        var result = await handler.Handle(At(0, 1), CancellationToken.None);

        Assert.Null(result);
    }

    // ── cursor on opening tag name ──────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnOpeningTagName_ReturnsTwoLinkedRanges()
    {
        // "<Foo>\nbar\n</Foo>"
        // line 0: <Foo> — 'F' at col 1
        // line 2: </Foo> — 'F' at col 2
        var handler = Build("<Foo>\nbar\n</Foo>");
        var result = await handler.Handle(At(0, 1), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Ranges.Count());
        var ranges = result.Ranges.OrderBy(r => r.Start.Line).ToList();

        // Opening tag name: line 0, cols 1–3 (exclusive end = 4 but length 3)
        Assert.Equal(0, ranges[0].Start.Line);
        Assert.Equal(1, ranges[0].Start.Character);
        Assert.Equal(0, ranges[0].End.Line);
        Assert.Equal(4, ranges[0].End.Character);

        // Closing tag name: line 2, cols 2–4
        Assert.Equal(2, ranges[1].Start.Line);
        Assert.Equal(2, ranges[1].Start.Character);
        Assert.Equal(2, ranges[1].End.Line);
        Assert.Equal(5, ranges[1].End.Character);
    }

    [Fact]
    public async Task Handle_CursorOnClosingTagName_ReturnsTwoLinkedRanges()
    {
        // Same document, cursor on the closing tag
        var handler = Build("<Foo>\nbar\n</Foo>");
        var result = await handler.Handle(At(2, 2), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Ranges.Count());
    }

    [Fact]
    public async Task Handle_CursorOnSingleLineTag_ReturnsTwoLinkedRanges()
    {
        // "<Root>\n<Max_Speed>500</Max_Speed>\n</Root>"
        // line 1: <Max_Speed>500</Max_Speed>
        // opening: col 1..9, closing: col 16..24
        var handler = Build("<Root>\n<Max_Speed>500</Max_Speed>\n</Root>");
        var result = await handler.Handle(At(1, 3), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Ranges.Count());
    }

    // ── cursor on content/attribute — returns null ───────────────────────────

    [Fact]
    public async Task Handle_CursorOnContent_ReturnsNull()
    {
        // Cursor on "bar" in "<Foo>\nbar\n</Foo>"
        var handler = Build("<Foo>\nbar\n</Foo>");
        var result = await handler.Handle(At(1, 0), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_CursorOnAngleBracket_ReturnsNull()
    {
        var handler = Build("<Foo>\nbar\n</Foo>");
        var result = await handler.Handle(At(0, 0), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_SelfClosingTag_ReturnsNull()
    {
        // Self-closing tags have no paired close tag — cannot do linked editing
        var handler = Build("<Root>\n<Foo/>\n</Root>");
        var result = await handler.Handle(At(1, 1), CancellationToken.None);

        Assert.Null(result);
    }

    // ── non-EaW file ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonEaWFile_ReturnsNull()
    {
        var host = new FakeHost(TestUri, "<Foo>\nbar\n</Foo>");
        var handler = new XmlLinkedEditingRangeHandler(TestParseCache.For(host), new DenyAllEaWContext(),
            new FileHelper(new MockFileSystem()), new FakeLspConfigurationProvider());
        var result = await handler.Handle(At(0, 1), CancellationToken.None);

        Assert.Null(result);
    }

    // ── word pattern ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_Result_IncludesWordPattern()
    {
        var handler = Build("<Foo>\nbar\n</Foo>");
        var result = await handler.Handle(At(0, 1), CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.WordPattern);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

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