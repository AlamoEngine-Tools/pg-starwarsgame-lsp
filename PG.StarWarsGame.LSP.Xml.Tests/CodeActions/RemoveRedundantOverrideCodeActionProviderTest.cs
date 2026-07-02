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

public sealed class RemoveRedundantOverrideCodeActionProviderTest
{
    private const string Uri = "file:///test/Units.xml";

    //  line 0: <Units>
    //  line 1:   <Unit Name="Base">
    //  line 2:     <Max_Speed>1.0</Max_Speed>
    //  line 3:   </Unit>
    //  line 4:   <Unit Name="Derived" Variant_Of_Existing_Type="Base">
    //  line 5:     <Max_Speed>1.0</Max_Speed>
    //  line 6:   </Unit>
    //  line 7: </Units>
    private const string Xml = """
                               <Units>
                                 <Unit Name="Base">
                                   <Max_Speed>1.0</Max_Speed>
                                 </Unit>
                                 <Unit Name="Derived" Variant_Of_Existing_Type="Base">
                                   <Max_Speed>1.0</Max_Speed>
                                 </Unit>
                               </Units>
                               """;

    private static RemoveRedundantOverrideCodeActionProvider BuildProvider(string xml)
    {
        var host = new FakeRemoveOverrideWorkspaceHost(Uri, xml);
        var fileHelper = new FileHelper(new MockFileSystem());
        return new RemoveRedundantOverrideCodeActionProvider(host, fileHelper);
    }

    private static XmlCodeActionContext MakeCtx(int diagnosticLine, bool withMarker)
    {
        var data = withMarker ? new JObject { ["removeRedundantOverride"] = true } : null;
        var diagnostic = new Diagnostic
        {
            Range = new LspRange(new Position(diagnosticLine, 4), new Position(diagnosticLine, 13)),
            Message = "redundant",
            Data = data
        };
        return new XmlCodeActionContext(DocumentUri.From(Uri), diagnostic);
    }

    [Fact]
    public void No_marker_returns_empty()
    {
        var provider = BuildProvider(Xml);
        Assert.Empty(provider.Handle(MakeCtx(5, false)));
    }

    [Fact]
    public void Marker_returns_single_quick_fix()
    {
        var provider = BuildProvider(Xml);

        var action = Assert.Single(provider.Handle(MakeCtx(5, true)));

        Assert.Equal(CodeActionKind.QuickFix, action.CodeAction!.Kind);
        Assert.Contains("Max_Speed", action.CodeAction!.Title);
    }

    [Fact]
    public void Quick_fix_deletes_the_whole_redundant_line()
    {
        var provider = BuildProvider(Xml);

        var action = provider.Handle(MakeCtx(5, true)).Single();
        var edit = Assert.Single(action.CodeAction!.Edit!.Changes!.Values.First());

        Assert.Equal("", edit.NewText);
        // Removes from the start of the redundant line (5) to the start of the next line (6).
        Assert.Equal(new Position(5, 0), edit.Range.Start);
        Assert.Equal(new Position(6, 0), edit.Range.End);
    }
}

file sealed class FakeRemoveOverrideWorkspaceHost : IGameWorkspaceHost
{
    private readonly string _normalizedUri;
    private readonly string _text;

    public FakeRemoveOverrideWorkspaceHost(string uri, string text)
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