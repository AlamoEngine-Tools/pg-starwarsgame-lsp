// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.CodeActions;
using PG.StarWarsGame.LSP.Xml.Validation;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeActions;

public sealed class XmlStructureRepairCodeActionProviderTest
{
    private const string Uri = "file:///test/Units.xml";

    //  line 0: <?xml version="1.0"?>
    //  line 1: <Root>
    //  line 2: \t<A>1</B>
    //  line 3: \t<C>2</C>``
    //  line 4: </Root>
    private const string Broken = "<?xml version=\"1.0\"?>\n<Root>\n\t<A>1</B>\n\t<C>2</C>``\n</Root>";

    private static XmlStructureRepairCodeActionProvider Build(string text, params DiagnosticId[] suppressed)
    {
        var host = new SingleDocumentHost(Uri, text);
        var fileHelper = new FileHelper(new MockFileSystem());
        return new XmlStructureRepairCodeActionProvider(TestParseCache.For(host, fileHelper), fileHelper,
            new XmlStructuralValidator(), new FixedSuppressions(suppressed.Select(SuppressionMatcher.ForId).ToList()));
    }

    private static XmlCodeActionContext At(DiagnosticId id, int line, int startCol, int endCol)
    {
        return new XmlCodeActionContext(DocumentUri.From(Uri), new Diagnostic
        {
            Code = id.ToString(),
            Range = new LspRange(new Position(line, startCol), new Position(line, endCol)),
            Message = "m"
        });
    }

    private static IReadOnlyList<CodeAction> Actions(XmlStructureRepairCodeActionProvider p, XmlCodeActionContext ctx)
    {
        return p.Handle(ctx).Select(a => a.CodeAction!).ToList();
    }

    [Fact]
    public void RepairableFinding_OffersItsRepairAsAQuickFix()
    {
        // The end tag </B> sits at line 2, columns 5-9.
        var actions = Actions(Build(Broken), At(DiagnosticIds.XmlEndTagMismatch, 2, 5, 9));

        var fix = Assert.Single(actions, a => a.Title == "Rename the end tag to </A>");
        Assert.Equal(CodeActionKind.QuickFix, fix.Kind);
        Assert.True(fix.IsPreferred);
        var edit = Assert.Single(fix.Edit!.Changes![DocumentUri.From(Uri)]);
        Assert.Equal(new LspRange(new Position(2, 7), new Position(2, 8)), edit.Range);
        Assert.Equal("A", edit.NewText);
    }

    [Fact]
    public void DiagnosticNotAtAFinding_OffersNothing()
    {
        // A stale range: nothing in the current text is found there.
        Assert.Empty(Actions(Build(Broken), At(DiagnosticIds.XmlEndTagMismatch, 4, 0, 2)));
    }

    [Fact]
    public void UnrelatedDiagnostic_OffersNothing()
    {
        Assert.Empty(Actions(Build(Broken), At(DiagnosticIds.DuplicateSymbol, 2, 5, 9)));
    }

    [Fact]
    public void FindingWithoutARepair_OffersNoQuickFix()
    {
        const string text = "<?xml version=\"1.0\"?>\n<Root>\n\t<A>Tom & Jerry</A>\n</Root>";
        var actions = Actions(Build(text), At(DiagnosticIds.XmlStrayAmpersand, 2, 9, 10));
        Assert.Empty(actions);
    }

    [Fact]
    public void FixAll_IsOfferedBesideTheQuickFix_AndRepairsTheWholeFile()
    {
        var actions = Actions(Build(Broken), At(DiagnosticIds.XmlEndTagMismatch, 2, 5, 9));

        var all = Assert.Single(actions, a => a.Title == "Fix all repairable XML structure problems in this file");
        var edit = Assert.Single(all.Edit!.Changes![DocumentUri.From(Uri)]);
        Assert.Equal("<?xml version=\"1.0\"?>\n<Root>\n\t<A>1</A>\n\t<C>2</C>\n</Root>", edit.NewText);
        Assert.Equal(new LspRange(new Position(0, 0), new Position(4, 7)), edit.Range);
    }

    [Fact]
    public void FixAll_LeavesSuppressedCategoriesAlone()
    {
        const string text = "<?xml version=\"1.0\"?>\n<Root>\n\t<List>A,\n\t\t<!--B,-->\n\t\tC</List>\n\t<D>1</D>``\n</Root>";
        var provider = Build(text, DiagnosticIds.XmlCommentInsideValue);

        var actions = Actions(provider, At(DiagnosticIds.XmlCharacterDataAfterChild, 5, 9, 11));

        var all = Assert.Single(actions, a => a.Title.StartsWith("Fix all", StringComparison.Ordinal));
        var newText = Assert.Single(all.Edit!.Changes![DocumentUri.From(Uri)]).NewText;
        Assert.Contains("<!--B,-->\n\t\tC", newText, StringComparison.Ordinal);
        Assert.DoesNotContain("``", newText, StringComparison.Ordinal);
    }

    private sealed class FixedSuppressions(IReadOnlyList<SuppressionMatcher> matchers) : IGlobalSuppressionStore
    {
        public IReadOnlyList<SuppressionMatcher> GetAll() => matchers;
        public void Add(SuppressionMatcher matcher, string? reason = null) { }
        public void Remove(SuppressionMatcher matcher) { }
    }
}

file sealed class SingleDocumentHost(string uri, string text) : IGameWorkspaceHost
{
    public IEnumerable<TrackedDocument> All => [new(uri, text, 1)];

    public void AddOrUpdate(string u, string t, int version, bool publishDiagnostics = true)
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
