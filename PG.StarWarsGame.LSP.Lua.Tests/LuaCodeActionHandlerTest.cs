// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Workspace;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Tests;

public sealed class LuaCodeActionHandlerTest
{
    private static LuaCodeActionHandler MakeSut(IGameWorkspaceHost? host = null)
    {
        return new LuaCodeActionHandler(host ?? new FakeWorkspaceHost());
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

    private static Diagnostic DuplicateRequireDiag(int line, int startChar, int endChar)
    {
        return new Diagnostic
        {
            Code = new DiagnosticCode(LuaDiagnosticCodes.DuplicateRequire),
            Range = new LspRange(new Position(line, startChar), new Position(line, endChar)),
            Severity = DiagnosticSeverity.Warning,
            Message = "require(\"x\") is a duplicate.",
            Source = AppProperties.LspServerId
        };
    }

    private static Diagnostic RedundantRequireDiag(int line, int startChar, int endChar)
    {
        return new Diagnostic
        {
            Code = new DiagnosticCode(LuaDiagnosticCodes.RedundantRequire),
            Range = new LspRange(new Position(line, startChar), new Position(line, endChar)),
            Severity = DiagnosticSeverity.Warning,
            Message = "require(\"x\") is redundant.",
            Source = AppProperties.LspServerId
        };
    }

    // ── basic dispatch ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_RedundantRequireDiagnostic_ReturnsOneQuickFix()
    {
        var diag = RedundantRequireDiag(3, 0, 12);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        var action = Assert.Single(result!).CodeAction;
        Assert.NotNull(action);
        Assert.Equal(CodeActionKind.QuickFix, action.Kind);
    }

    [Fact]
    public async Task Handle_OtherDiagnostic_ReturnsNoActions()
    {
        var otherDiag = new Diagnostic
        {
            Code = new DiagnosticCode("some-other-code"),
            Range = new LspRange(new Position(0, 0), new Position(0, 5)),
            Message = "some other problem"
        };
        var request = ParamsWithDiagnostics("file:///s.lua", otherDiag);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        Assert.Empty(result!);
    }

    [Fact]
    public async Task Handle_NoDiagnostics_ReturnsEmpty()
    {
        var request = ParamsWithDiagnostics("file:///s.lua");

        var result = await MakeSut().Handle(request, CancellationToken.None);

        Assert.Empty(result!);
    }

    // ── edit content ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_RedundantDiagnostic_EditDeletesEntireLine()
    {
        // Diagnostic is on line 5 — edit must cover (5,0)..(6,0) to delete the full line.
        var diag = RedundantRequireDiag(5, 0, 14);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        var edit = result!.Single().CodeAction!.Edit!
            .Changes![DocumentUri.From("file:///s.lua")].Single();

        Assert.Equal(5, edit.Range.Start.Line);
        Assert.Equal(0, edit.Range.Start.Character);
        Assert.Equal(6, edit.Range.End.Line);
        Assert.Equal(0, edit.Range.End.Character);
        Assert.Equal("", edit.NewText);
    }

    [Fact]
    public async Task Handle_RedundantDiagnostic_TitleIsRemoveRedundantRequire()
    {
        var diag = RedundantRequireDiag(0, 0, 12);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        Assert.Equal("Remove redundant require", result!.Single().CodeAction!.Title);
    }

    [Fact]
    public async Task Handle_RedundantDiagnostic_IsPreferred()
    {
        var diag = RedundantRequireDiag(0, 0, 12);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        Assert.True(result!.Single().CodeAction!.IsPreferred);
    }

    [Fact]
    public async Task Handle_MixedDiagnostics_OnlyRedundantGetsAction()
    {
        var redundant = RedundantRequireDiag(2, 0, 12);
        var other = new Diagnostic
        {
            Code = new DiagnosticCode("something-else"),
            Range = new LspRange(new Position(3, 0), new Position(3, 5)),
            Message = "unrelated"
        };
        var request = ParamsWithDiagnostics("file:///s.lua", redundant, other);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        Assert.Single(result!);
    }

    // ── duplicate require quick-fix ──────────────────────────────────────────

    [Fact]
    public async Task Handle_DuplicateRequireDiagnostic_ReturnsOneQuickFix()
    {
        var diag = DuplicateRequireDiag(2, 0, 12);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        var action = Assert.Single(result!).CodeAction;
        Assert.NotNull(action);
        Assert.Equal(CodeActionKind.QuickFix, action!.Kind);
    }

    [Fact]
    public async Task Handle_DuplicateRequireDiagnostic_EditDeletesEntireLine()
    {
        var diag = DuplicateRequireDiag(3, 0, 12);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        var edit = result!.Single().CodeAction!.Edit!
            .Changes![DocumentUri.From("file:///s.lua")].Single();

        Assert.Equal(3, edit.Range.Start.Line);
        Assert.Equal(0, edit.Range.Start.Character);
        Assert.Equal(4, edit.Range.End.Line);
        Assert.Equal(0, edit.Range.End.Character);
        Assert.Equal("", edit.NewText);
    }

    [Fact]
    public async Task Handle_DuplicateRequireDiagnostic_TitleIsRemoveDuplicateRequire()
    {
        var diag = DuplicateRequireDiag(0, 0, 12);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        Assert.Equal("Remove duplicate require", result!.Single().CodeAction!.Title);
    }

    [Fact]
    public async Task Handle_DuplicateRequireDiagnostic_IsPreferred()
    {
        var diag = DuplicateRequireDiag(0, 0, 12);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        Assert.True(result!.Single().CodeAction!.IsPreferred);
    }

    // ── upvalue quick fixes ──────────────────────────────────────────────────

    // "local x = 1" on line 0, "function Foo()" on line 1, "    return x" on line 2
    private static Diagnostic EngineUpvalueDiag(
        int useLine, int useChar, int useEnd,
        int declLine, int funcDeclLine,
        string docUri = "file:///s.lua")
    {
        return new Diagnostic
        {
            Code = new DiagnosticCode(LuaDiagnosticCodes.EngineUpvalue),
            Range = new LspRange(new Position(useLine, useChar), new Position(useLine, useEnd)),
            Severity = DiagnosticSeverity.Warning,
            Message = $"File-level local 'x' is captured as an upvalue by 'Foo'.",
            Source = AppProperties.LspServerId,
            RelatedInformation = new Container<DiagnosticRelatedInformation>(
                new DiagnosticRelatedInformation
                {
                    Location = new Location
                    {
                        Uri = DocumentUri.From(docUri),
                        Range = new LspRange(new Position(declLine, 0), new Position(declLine, 12))
                    },
                    Message = "local declaration"
                },
                new DiagnosticRelatedInformation
                {
                    Location = new Location
                    {
                        Uri = DocumentUri.From(docUri),
                        Range = new LspRange(new Position(funcDeclLine, 0), new Position(funcDeclLine, 14))
                    },
                    Message = "function declaration"
                })
        };
    }

    [Fact]
    public async Task Handle_EngineUpvalueDiagnostic_ReturnsSuppressAnnotationAction()
    {
        var diag = EngineUpvalueDiag(useLine: 2, useChar: 11, useEnd: 12, declLine: 0, funcDeclLine: 1);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        Assert.Contains(result!, a => a.CodeAction?.Title?.Contains("upvalue-ok") == true);
    }

    [Fact]
    public async Task Handle_EngineUpvalueDiagnostic_SuppressAction_InsertsAnnotationAboveDeclaration()
    {
        // local is on line 0 → annotation insert is (0,0)-(0,0) with "---@upvalue-ok\n"
        var diag = EngineUpvalueDiag(useLine: 2, useChar: 11, useEnd: 12, declLine: 0, funcDeclLine: 1);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        var action = result!.First(a => a.CodeAction?.Title?.Contains("upvalue-ok") == true).CodeAction!;
        var edit = action.Edit!.Changes![DocumentUri.From("file:///s.lua")].Single();
        Assert.Equal(0, edit.Range.Start.Line);
        Assert.Equal(0, edit.Range.Start.Character);
        Assert.Equal(0, edit.Range.End.Line);
        Assert.Equal(0, edit.Range.End.Character);
        Assert.Equal("---@upvalue-ok\n", edit.NewText);
    }

    [Fact]
    public async Task Handle_EngineUpvalueDiagnostic_OnlyOneCapturingFunction_ReturnsMoveLocalAction()
    {
        const string docText = "local x = 1\nfunction Foo()\n    return x\nend";
        var host = new FakeWorkspaceHost();
        host.Set("file:///s.lua", docText);
        var diag = EngineUpvalueDiag(useLine: 2, useChar: 11, useEnd: 12, declLine: 0, funcDeclLine: 1);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut(host).Handle(request, CancellationToken.None);

        Assert.Contains(result!, a => a.CodeAction?.Title?.Contains("Move") == true ||
                                      a.CodeAction?.Title?.Contains("move") == true ||
                                      a.CodeAction?.Title?.Contains("inside") == true);
    }

    [Fact]
    public async Task Handle_EngineUpvalueDiagnostic_TwoCapturingFunctions_NoMoveLocalAction()
    {
        // Two separate diagnostics reference the same local (declLine 0) → captured by 2 functions
        var diag1 = EngineUpvalueDiag(useLine: 2, useChar: 11, useEnd: 12, declLine: 0, funcDeclLine: 1);
        var diag2 = EngineUpvalueDiag(useLine: 5, useChar: 11, useEnd: 12, declLine: 0, funcDeclLine: 4);
        var request = ParamsWithDiagnostics("file:///s.lua", diag1, diag2);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        Assert.DoesNotContain(result!, a => a.CodeAction?.Title?.Contains("inside") == true);
    }

    [Fact]
    public async Task Handle_EngineUpvalueDiagnostic_MoveAction_IsPreferred()
    {
        const string docText = "local x = 1\nfunction Foo()\n    return x\nend";
        var host = new FakeWorkspaceHost();
        host.Set("file:///s.lua", docText);
        var diag = EngineUpvalueDiag(useLine: 2, useChar: 11, useEnd: 12, declLine: 0, funcDeclLine: 1);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut(host).Handle(request, CancellationToken.None);

        var moveAction = result!.First(a => a.CodeAction?.Title?.Contains("inside") == true).CodeAction!;
        Assert.True(moveAction.IsPreferred);
    }

    [Fact]
    public async Task Handle_EngineUpvalueDiagnostic_MoveAction_DeletesDeclarationLine()
    {
        // local on line 0 → delete (0,0)-(1,0)
        const string docText = "local x = 1\nfunction Foo()\n    return x\nend";
        var host = new FakeWorkspaceHost();
        host.Set("file:///s.lua", docText);
        var diag = EngineUpvalueDiag(useLine: 2, useChar: 11, useEnd: 12, declLine: 0, funcDeclLine: 1);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut(host).Handle(request, CancellationToken.None);

        var edits = result!.First(a => a.CodeAction?.Title?.Contains("inside") == true)
            .CodeAction!.Edit!.Changes![DocumentUri.From("file:///s.lua")].ToList();
        Assert.Contains(edits, e =>
            e.Range.Start.Line == 0 && e.Range.Start.Character == 0 &&
            e.Range.End.Line == 1 && e.Range.End.Character == 0 &&
            e.NewText == "");
    }

    [Fact]
    public async Task Handle_EngineUpvalueDiagnostic_MoveAction_InsertsDeclarationAtFunctionBodyStart()
    {
        // function on line 1 → body starts at line 2 → insert at (2,0)
        const string docText = "local x = 1\nfunction Foo()\n    return x\nend";
        var host = new FakeWorkspaceHost();
        host.Set("file:///s.lua", docText);
        var diag = EngineUpvalueDiag(useLine: 2, useChar: 11, useEnd: 12, declLine: 0, funcDeclLine: 1);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut(host).Handle(request, CancellationToken.None);

        var edits = result!.First(a => a.CodeAction?.Title?.Contains("inside") == true)
            .CodeAction!.Edit!.Changes![DocumentUri.From("file:///s.lua")].ToList();
        Assert.Contains(edits, e =>
            e.Range.Start.Line == 2 && e.Range.Start.Character == 0 &&
            e.Range.End.Line == 2 && e.Range.End.Character == 0 &&
            e.NewText == "    local x = 1\n");
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string uri, string text) =>
            _docs[uri] = new TrackedDocument(uri, text, 1);

        public void AddOrUpdate(string uri, string text, int version) =>
            _docs[uri] = new TrackedDocument(uri, text, version);

        public bool TryGet(string uri, out TrackedDocument doc) =>
            _docs.TryGetValue(uri, out doc!);

        public void Remove(string uri) => _docs.Remove(uri);

        public IEnumerable<TrackedDocument> All => _docs.Values;
    }
}