// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Xml.CodeActions;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlCodeActionHandlerTest
{
    private static XmlCodeActionHandler MakeSut(IXmlFixCache? cache = null,
        ILspConfigurationProvider? config = null)
    {
        var resolvedConfig = config ?? new FakeLspConfigurationProvider();
        var registry = new XmlCodeActionRegistry([
            new FixSuggestionCodeActionProvider(cache ?? new EmptyFixCache()),
            new CreateLocKeyCodeActionProvider(resolvedConfig)
        ]);
        return new XmlCodeActionHandler(registry, resolvedConfig);
    }

    // ── feature flag ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CodeActionsFlagOff_ReturnsEmpty()
    {
        // Same arrange as DiagnosticWithFix_ReturnsSingleCodeActionWithEdit — only the flag differs.
        const string uri = "file:///test.xml";
        var range = new LspRange(new Position(0, 10), new Position(0, 13));
        var request = ParamsWithDiagnostics(uri, DiagWithFix(range, "42"));
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Xml = new XmlFeatureFlags { CodeActions = false } });

        var result = await MakeSut(config: config).Handle(request, CancellationToken.None);

        Assert.Empty(result!);
    }

    private static CodeActionParams ParamsWithDiagnostics(string uri, params Diagnostic[] diagnostics)
    {
        return new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Range = new LspRange(new Position(0, 0), new Position(0, 0)),
            Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>(diagnostics) }
        };
    }

    private static Diagnostic DiagWithFix(LspRange range, string fix)
    {
        return new Diagnostic { Range = range, Data = JToken.FromObject(new { fix }) };
    }

    private static Diagnostic DiagWithoutFix(LspRange range)
    {
        return new Diagnostic { Range = range, Data = null };
    }

    [Fact]
    public async Task DiagnosticWithFix_ReturnsSingleCodeActionWithEdit()
    {
        const string uri = "file:///test.xml";
        var range = new LspRange(new Position(0, 10), new Position(0, 13));
        var request = ParamsWithDiagnostics(uri, DiagWithFix(range, "42"));

        var result = await MakeSut().Handle(request, CancellationToken.None);

        var action = Assert.Single(result!).CodeAction;
        Assert.NotNull(action);
        Assert.Equal(CodeActionKind.QuickFix, action.Kind);
        Assert.Equal("Replace with '42'", action.Title);
        Assert.True(action.IsPreferred);

        var docUri = DocumentUri.From(uri);
        var edit = Assert.Single(action.Edit!.Changes![docUri].ToList());
        Assert.Equal("42", edit.NewText);
        Assert.Equal(range, edit.Range);
    }

    [Fact]
    public async Task DiagnosticWithoutFix_NoCache_ReturnsEmpty()
    {
        var range = new LspRange(new Position(1, 5), new Position(1, 8));
        var request = ParamsWithDiagnostics("file:///test.xml", DiagWithoutFix(range));

        var result = await MakeSut().Handle(request, CancellationToken.None);

        Assert.Empty(result!);
    }

    [Fact]
    public async Task DiagnosticDataAbsent_CacheFallback_ReturnsAction()
    {
        const string uri = "file:///test.xml";
        var range = new LspRange(new Position(0, 10), new Position(0, 13));
        var cache = new StubFixCache(uri, 0, 10, "99");
        var request = ParamsWithDiagnostics(uri, DiagWithoutFix(range));

        var result = await MakeSut(cache).Handle(request, CancellationToken.None);

        var action = Assert.Single(result!).CodeAction;
        Assert.Equal("Replace with '99'", action!.Title);
    }

    [Fact]
    public async Task MultipleFixableDiagnostics_ReturnsOneActionEach()
    {
        const string uri = "file:///test.xml";
        var range1 = new LspRange(new Position(0, 5), new Position(0, 8));
        var range2 = new LspRange(new Position(2, 3), new Position(2, 6));
        var request = ParamsWithDiagnostics(uri,
            DiagWithFix(range1, "1"),
            DiagWithFix(range2, "2"));

        var result = await MakeSut().Handle(request, CancellationToken.None);

        var actions = result!.ToList();
        Assert.Equal(2, actions.Count);
        var docUri = DocumentUri.From(uri);
        Assert.Equal("1", actions[0].CodeAction!.Edit!.Changes![docUri].Single().NewText);
        Assert.Equal("2", actions[1].CodeAction!.Edit!.Changes![docUri].Single().NewText);
    }

    [Fact]
    public async Task DiagnosticWithCreateLocKey_ReturnsCommandAction()
    {
        const string uri = "file:///test.xml";
        var range = new LspRange(new Position(3, 4), new Position(3, 16));
        var d = DiagWithCreateLocKey(range, "TEXT_FOO");
        var request = ParamsWithDiagnostics(uri, d);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        var action = Assert.Single(result!).CodeAction;
        Assert.NotNull(action);
        Assert.Equal(CodeActionKind.QuickFix, action.Kind);
        Assert.Contains("TEXT_FOO", action.Title);
        Assert.NotNull(action.Command);
        Assert.Null(action.Edit);
    }

    [Fact]
    public async Task DiagnosticWithCreateLocKey_CommandNameAndArgAreCorrect()
    {
        const string uri = "file:///test.xml";
        var range = new LspRange(new Position(3, 4), new Position(3, 16));
        var request = ParamsWithDiagnostics(uri, DiagWithCreateLocKey(range, "TEXT_BAR"));

        var result = await MakeSut().Handle(request, CancellationToken.None);

        var command = result!.Single().CodeAction!.Command!;
        Assert.Equal("aet-eaw-edit.lsp.createLocalisationKey", command.Name);
        var arg = command.Arguments![0];
        Assert.Equal("TEXT_BAR", arg.Value<string>());
    }

    [Fact]
    public async Task DiagnosticWithBothFixAndCreateLocKey_ReturnsTwoActions()
    {
        const string uri = "file:///test.xml";
        var range = new LspRange(new Position(0, 0), new Position(0, 8));
        var d = new Diagnostic
        {
            Range = range,
            Data = JToken.FromObject(new { fix = "suggested", createLocKey = "TEXT_BOTH" })
        };
        var request = ParamsWithDiagnostics(uri, d);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        Assert.Equal(2, result!.Count());
    }

    private static Diagnostic DiagWithCreateLocKey(LspRange range, string key)
    {
        return new Diagnostic { Range = range, Data = JToken.FromObject(new { createLocKey = key }) };
    }

    private sealed class EmptyFixCache : IXmlFixCache
    {
        public string? GetSuggestedFix(string uri, int startLine, int startChar)
        {
            return null;
        }
    }

    private sealed class StubFixCache(string uri, int line, int ch, string fix) : IXmlFixCache
    {
        public string? GetSuggestedFix(string u, int l, int c)
        {
            return u == uri && l == line && c == ch ? fix : null;
        }
    }
}