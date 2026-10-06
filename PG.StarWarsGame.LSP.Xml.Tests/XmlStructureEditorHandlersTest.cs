// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;
using PG.StarWarsGame.LSP.Xml.Util;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Tests;

/// <summary>Folding, selection range and matching-tag highlight, on well-formed and malformed documents.</summary>
public sealed class XmlStructureEditorHandlersTest
{
    private const string Uri = "file:///test.xml";

    //  0 <?xml version="1.0"?>
    //  1 <Root>
    //  2 \t<!-- a
    //  3 \t     b -->
    //  4 \t<Unit Name="x">
    //  5 \t\t<Max_Speed>1</Max_Speed>
    //  6 \t</Unit>
    //  7 </Root>
    private const string WellFormed =
        "<?xml version=\"1.0\"?>\n<Root>\n\t<!-- a\n\t     b -->\n\t<Unit Name=\"x\">\n\t\t<Max_Speed>1</Max_Speed>\n\t</Unit>\n</Root>";

    //  0 <Root>
    //  1 \t<A>
    //  2 \t<B>
    //  3 \t\t<C>1</C>
    //  4 \t</B>
    //  5 </Root>
    private const string MissingClose = "<Root>\n\t<A>\n\t<B>\n\t\t<C>1</C>\n\t</B>\n</Root>";

    private static TextDocumentIdentifier Doc => new() { Uri = DocumentUri.From(Uri) };

    private static (IXmlParseCache Cache, IFileHelper Files) Parts(string text)
    {
        var files = new FileHelper(new MockFileSystem());
        return (TestParseCache.For(new Host(Uri, text), files), files);
    }

    // ── folding ─────────────────────────────────────────────────────────────

    private static async Task<List<FoldingRange>> Folds(string text)
    {
        var (cache, files) = Parts(text);
        var handler = new XmlFoldingRangeHandler(cache, new AllowAllEaWContext(), files);
        var result = await handler.Handle(new FoldingRangeRequestParam { TextDocument = Doc }, CancellationToken.None);
        return result!.ToList();
    }

    [Fact]
    public async Task Folding_MultiLineElementsFoldToTheLineBeforeTheirEndTag()
    {
        var folds = await Folds(WellFormed);

        Assert.Contains(folds, f => f is { StartLine: 1, EndLine: 6, Kind: null });
        Assert.Contains(folds, f => f is { StartLine: 4, EndLine: 5, Kind: null });
        Assert.DoesNotContain(folds, f => f.StartLine == 5); // single-line element
    }

    [Fact]
    public async Task Folding_MultiLineCommentIsACommentFold()
    {
        var folds = await Folds(WellFormed);
        Assert.Contains(folds, f => f.StartLine == 2 && f.EndLine == 3 && f.Kind == FoldingRangeKind.Comment);
    }

    [Fact]
    public async Task Folding_MalformedDocument_UnclosedElementGetsNoFold_OthersDo()
    {
        var folds = await Folds(MissingClose);

        Assert.DoesNotContain(folds, f => f.StartLine == 1);
        Assert.Contains(folds, f => f is { StartLine: 2, EndLine: 3 });
        Assert.Contains(folds, f => f is { StartLine: 0, EndLine: 4 });
    }

    // ── selection range ─────────────────────────────────────────────────────

    private static async Task<SelectionRange> Selection(string text, int line, int character)
    {
        var (cache, files) = Parts(text);
        var handler = new XmlSelectionRangeHandler(cache, new AllowAllEaWContext(), files);
        var result = await handler.Handle(new SelectionRangeParams
        {
            TextDocument = Doc, Positions = new Container<Position>(new Position(line, character))
        }, CancellationToken.None);
        return Assert.Single(result!);
    }

    private static List<Range> Chain(SelectionRange s)
    {
        var list = new List<Range>();
        for (var r = s; r is not null; r = r.Parent) list.Add(r.Range);
        return list;
    }

    [Fact]
    public void Selection_OnATagName_GrowsNameElementParentRoot()
    {
        var chain = Chain(Selection(WellFormed, 5, 4).Result);

        Assert.Equal(new Range(5, 3, 5, 12), chain[0]); // Max_Speed
        Assert.Equal(new Range(5, 2, 5, 26), chain[1]); // <Max_Speed>1</Max_Speed>
        Assert.Equal(new Range(4, 1, 6, 8), chain[2]); // <Unit ...> ... </Unit>
        Assert.Equal(new Range(1, 0, 7, 7), chain[3]); // <Root> ... </Root>
        Assert.Equal(4, chain.Count);
    }

    [Fact]
    public void Selection_InAValue_StartsAtTheElement()
    {
        var chain = Chain(Selection(WellFormed, 5, 13).Result);
        Assert.Equal(new Range(5, 2, 5, 26), chain[0]);
    }

    [Fact]
    public void Selection_InAMalformedDocument_StillGrows()
    {
        var chain = Chain(Selection(MissingClose, 3, 3).Result);
        Assert.Equal(new Range(3, 3, 3, 4), chain[0]); // C
        Assert.Equal(new Range(3, 2, 3, 10), chain[1]); // <C>1</C>
        Assert.Contains(new Range(0, 0, 5, 7), chain); // Root
    }

    // ── document highlight ──────────────────────────────────────────────────

    private static async Task<List<DocumentHighlight>> Highlights(string text, int line, int character)
    {
        var (cache, files) = Parts(text);
        var handler = new XmlDocumentHighlightHandler(cache, new AllowAllEaWContext(), files);
        var result = await handler.Handle(new DocumentHighlightParams
        {
            TextDocument = Doc, Position = new Position(line, character)
        }, CancellationToken.None);
        return result?.ToList() ?? [];
    }

    [Fact]
    public async Task Highlight_OnAStartTagName_MarksBothNames()
    {
        var marks = await Highlights(WellFormed, 4, 3);

        Assert.Equal([new Range(4, 2, 4, 6), new Range(6, 3, 6, 7)],
            marks.Select(m => m.Range).OrderBy(r => r.Start.Line).ToList());
        Assert.All(marks, m => Assert.Equal(DocumentHighlightKind.Text, m.Kind));
    }

    [Fact]
    public async Task Highlight_OnAnEndTagName_MarksBothNames()
    {
        Assert.Equal(2, (await Highlights(WellFormed, 6, 4)).Count);
    }

    [Fact]
    public async Task Highlight_OnAnUnclosedElement_MarksItsNameOnly()
    {
        var mark = Assert.Single(await Highlights(MissingClose, 1, 2));
        Assert.Equal(new Range(1, 2, 1, 3), mark.Range);
    }

    [Fact]
    public async Task Highlight_OffAnyTagName_MarksNothing()
    {
        Assert.Empty(await Highlights(WellFormed, 5, 13));
    }

    private sealed class Host(string uri, string text) : IGameWorkspaceHost
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
            doc = new TrackedDocument(uri, text, 1);
            return true;
        }
    }
}
