// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Xml.CodeActions;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeActions;

public sealed class FixSuggestionCodeActionProviderTest
{
    private static readonly DocumentUri TestUri = DocumentUri.From("file:///test.xml");
    private static readonly LspRange TestRange = new(new Position(0, 5), new Position(0, 10));

    private static XmlCodeActionContext Ctx(Diagnostic d) => new(TestUri, d);

    [Fact]
    public void DiagnosticWithDataFix_ReturnsReplaceAction()
    {
        var d = new Diagnostic { Range = TestRange, Data = JToken.FromObject(new { fix = "42" }) };
        var provider = new FixSuggestionCodeActionProvider(new EmptyFixCache());

        var results = provider.Handle(Ctx(d)).ToList();

        var action = Assert.Single(results).CodeAction!;
        Assert.Equal("Replace with '42'", action.Title);
        Assert.Equal(CodeActionKind.QuickFix, action.Kind);
        Assert.True(action.IsPreferred);
        var edit = Assert.Single(action.Edit!.Changes![TestUri].ToList());
        Assert.Equal("42", edit.NewText);
        Assert.Equal(TestRange, edit.Range);
    }

    [Fact]
    public void DiagnosticWithCachedFix_ReturnsReplaceAction()
    {
        var d = new Diagnostic { Range = TestRange, Data = null };
        var cache = new StubFixCache(fix: "cached");
        var provider = new FixSuggestionCodeActionProvider(cache);

        var results = provider.Handle(Ctx(d)).ToList();

        var action = Assert.Single(results).CodeAction!;
        Assert.Equal("Replace with 'cached'", action.Title);
    }

    [Fact]
    public void NoFixInDataOrCache_ReturnsEmpty()
    {
        var d = new Diagnostic { Range = TestRange, Data = null };
        var provider = new FixSuggestionCodeActionProvider(new EmptyFixCache());

        var results = provider.Handle(Ctx(d)).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void DataFixTakesPrecedenceOverCache()
    {
        var d = new Diagnostic { Range = TestRange, Data = JToken.FromObject(new { fix = "data-fix" }) };
        var cache = new StubFixCache(fix: "cache-fix");
        var provider = new FixSuggestionCodeActionProvider(cache);

        var results = provider.Handle(Ctx(d)).ToList();

        Assert.Single(results);
        Assert.Equal("Replace with 'data-fix'", results[0].CodeAction!.Title);
    }
}

// ── file-scoped helpers ──────────────────────────────────────────────────────

file sealed class EmptyFixCache : IXmlFixCache
{
    public string? GetSuggestedFix(string uri, int line, int character) => null;
    public void SetSuggestedFix(string uri, int line, int character, string fix) { }
    public void ClearSuggestedFixes(string uri) { }
}

file sealed class StubFixCache : IXmlFixCache
{
    private readonly string? _fix;
    public StubFixCache(string? fix) => _fix = fix;
    public string? GetSuggestedFix(string uri, int line, int character) => _fix;
    public void SetSuggestedFix(string uri, int line, int character, string fix) { }
    public void ClearSuggestedFixes(string uri) { }
}
