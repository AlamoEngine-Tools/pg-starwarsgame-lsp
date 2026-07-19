// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.CodeActions;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeActions;

public sealed class RemoveEarlierDuplicatesCodeActionProviderTest
{
    private const string Uri = "file:///test/Units.xml";

    //  line 0: <Units>
    //  line 1:   <Unit Name="A">
    //  line 2:     <Icon_Name>first.tga</Icon_Name>
    //  line 3:     <Max_Speed>1.0</Max_Speed>
    //  line 4:     <Icon_Name>second.tga</Icon_Name>
    //  line 5:     <Icon_Name>winner.tga</Icon_Name>
    //  line 6:   </Unit>
    //  line 7: </Units>
    private const string Xml = """
                               <Units>
                                 <Unit Name="A">
                                   <Icon_Name>first.tga</Icon_Name>
                                   <Max_Speed>1.0</Max_Speed>
                                   <Icon_Name>second.tga</Icon_Name>
                                   <Icon_Name>winner.tga</Icon_Name>
                                 </Unit>
                               </Units>
                               """;

    private static RemoveEarlierDuplicatesCodeActionProvider BuildProvider(string xml)
    {
        var host = new FakeDuplicatesWorkspaceHost(Uri, xml);
        var fileHelper = new FileHelper(new MockFileSystem());
        return new RemoveEarlierDuplicatesCodeActionProvider(TestParseCache.For(host, fileHelper), fileHelper);
    }

    private static XmlCodeActionContext MakeCtx(int diagnosticLine, bool withMarker)
    {
        var data = withMarker ? new JObject { ["removeEarlierDuplicates"] = true } : null;
        var diagnostic = new Diagnostic
        {
            Range = new LspRange(new Position(diagnosticLine, 4), new Position(diagnosticLine, 13)),
            Message = "duplicate",
            Data = data
        };
        return new XmlCodeActionContext(DocumentUri.From(Uri), diagnostic);
    }

    [Fact]
    public void No_marker_returns_empty()
    {
        Assert.Empty(BuildProvider(Xml).Handle(MakeCtx(5, false)));
    }

    [Fact]
    public void Marker_on_last_occurrence_removes_both_earlier_lines()
    {
        var action = Assert.Single(BuildProvider(Xml).Handle(MakeCtx(5, true)));

        Assert.Equal(CodeActionKind.QuickFix, action.CodeAction!.Kind);
        Assert.Contains("Icon_Name", action.CodeAction!.Title);

        var edits = action.CodeAction!.Edit!.Changes!.Values.First().ToList();
        Assert.Equal(2, edits.Count);
        Assert.All(edits, e => Assert.Equal("", e.NewText));
        // Whole-line deletions of the two earlier occurrences (lines 2 and 4).
        Assert.Contains(edits, e => e.Range.Start == new Position(2, 0) && e.Range.End == new Position(3, 0));
        Assert.Contains(edits, e => e.Range.Start == new Position(4, 0) && e.Range.End == new Position(5, 0));
    }

    [Fact]
    public void Marker_on_earlier_occurrence_offers_the_same_fix()
    {
        // Triggering the fix from an earlier (greyed-out) occurrence must keep the LAST one too —
        // "keep last" semantics are independent of which diagnostic the user invoked it from.
        var action = Assert.Single(BuildProvider(Xml).Handle(MakeCtx(2, true)));

        var edits = action.CodeAction!.Edit!.Changes!.Values.First().ToList();
        Assert.Equal(2, edits.Count);
        Assert.DoesNotContain(edits, e => e.Range.Start.Line == 5); // the winner stays
    }
}

file sealed class FakeDuplicatesWorkspaceHost : IGameWorkspaceHost
{
    private readonly string _normalizedUri;
    private readonly string _text;

    public FakeDuplicatesWorkspaceHost(string uri, string text)
    {
        _normalizedUri = uri;
        _text = text;
    }

    public IEnumerable<TrackedDocument> All => [new(_normalizedUri, _text, 1)];

    public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
    {
    }

    public void Remove(string uri)
    {
    }

    public bool TryGet(string uri, out TrackedDocument doc)
    {
        doc = new TrackedDocument(_normalizedUri, _text, 1);
        return true;
    }
}