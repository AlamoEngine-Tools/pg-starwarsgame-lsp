// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class XmlFormattingHandlersTest
{
    private const string Uri = "file:///test.xml";

    //  0 <?xml version="1.0"?>
    //  1 <Root>
    //  2 <A>1</A>
    //  3 <B>2</B>
    //  4 </Root>
    private const string Text = "<?xml version=\"1.0\"?>\n<Root>\n<A>1</A>\n<B>2</B>\n</Root>";

    private readonly RecordingUserNotifier _notifier = new();

    private static TextDocumentIdentifier Doc => new() { Uri = DocumentUri.From(Uri) };

    private static FormattingOptions Tabs => new() { TabSize = 4, InsertSpaces = false };

    private static List<TextEdit> All(TextEditContainer? edits)
    {
        return edits?.ToList() ?? [];
    }

    private (XmlDocumentFormattingHandler Full, XmlDocumentRangeFormattingHandler Partial) Handlers(
        string text, bool flag = true, bool eaw = true)
    {
        var cache = new OneDocumentCache(text);
        var context = new FixedEaWContext(eaw);
        var files = new FileHelper(new MockFileSystem());
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Xml = new XmlFeatureFlags { Formatting = flag } });
        return (new XmlDocumentFormattingHandler(cache, context, files, _notifier, config),
            new XmlDocumentRangeFormattingHandler(cache, context, files, _notifier, config));
    }

    [Fact]
    public async Task Full_IndentsWithTheEditorsTabs()
    {
        var edits = await Handlers(Text).Full.Handle(
            new DocumentFormattingParams { TextDocument = Doc, Options = Tabs }, CancellationToken.None);

        Assert.Equal<(Range, string)>([(new Range(2, 0, 2, 0), "\t"), (new Range(3, 0, 3, 0), "\t")],
            All(edits).OrderBy(e => e.Range.Start.Line).Select(e => (e.Range, e.NewText)).ToList());
    }

    [Fact]
    public async Task Full_IndentsWithTheEditorsSpaces()
    {
        var edits = await Handlers(Text).Full.Handle(new DocumentFormattingParams
        {
            TextDocument = Doc, Options = new FormattingOptions { TabSize = 2, InsertSpaces = true }
        }, CancellationToken.None);

        Assert.All(All(edits), e => Assert.Equal<string>("  ", e.NewText));
    }

    [Fact]
    public async Task Range_EditsOnlyTheSelectedLines()
    {
        var edits = await Handlers(Text).Partial.Handle(new DocumentRangeFormattingParams
        {
            TextDocument = Doc, Options = Tabs, Range = new Range(3, 0, 3, 8)
        }, CancellationToken.None);

        var edit = Assert.Single(All(edits));
        Assert.Equal(new Range(3, 0, 3, 0), edit.Range);
    }

    [Fact]
    public async Task Malformed_IsRefused_WithAWarning()
    {
        var edits = await Handlers("<?xml version=\"1.0\"?>\n<Root>\n<A>1</B>\n</Root>").Full.Handle(
            new DocumentFormattingParams { TextDocument = Doc, Options = Tabs }, CancellationToken.None);

        Assert.Empty(All(edits));
        var warning = Assert.Single(_notifier.Warnings);
        Assert.StartsWith("Formatting refused: ", warning);
    }

    [Fact]
    public async Task FlagOff_FormatsNothing()
    {
        var edits = await Handlers(Text, flag: false).Full.Handle(
            new DocumentFormattingParams { TextDocument = Doc, Options = Tabs }, CancellationToken.None);

        Assert.Empty(All(edits));
        Assert.Empty(_notifier.Warnings);
    }

    [Fact]
    public async Task NonGameXml_FormatsNothing()
    {
        var edits = await Handlers(Text, eaw: false).Full.Handle(
            new DocumentFormattingParams { TextDocument = Doc, Options = Tabs }, CancellationToken.None);

        Assert.Empty(All(edits));
    }

    private sealed class OneDocumentCache(string text) : IXmlParseCache
    {
        public ParsedXmlDocument GetOrParse(string canonicalUri, string current)
        {
            return ParsedXmlDocument.Parse(current);
        }

        public ParsedXmlDocument GetOrParse(string canonicalUri)
        {
            return ParsedXmlDocument.Parse(text);
        }
    }

    private sealed class FixedEaWContext(bool eaw) : IEaWXmlContext
    {
        public bool HasDirectories => true;

        public bool IsEaWXmlFile(string fileUri)
        {
            return eaw;
        }

        public bool IsLeafFile(string fileUri)
        {
            return eaw;
        }

        public string? TryGetXmlRelativePath(string fileUri)
        {
            return null;
        }

        public void AddDirectory(string absolutePath)
        {
        }

        public void SetDirectories(IEnumerable<string> absolutePaths)
        {
        }

        public void SetLeafDirectories(IEnumerable<string> absolutePaths)
        {
        }
    }
}