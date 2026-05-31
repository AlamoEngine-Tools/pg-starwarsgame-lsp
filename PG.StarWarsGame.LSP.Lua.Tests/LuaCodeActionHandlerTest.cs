// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Tests;

public sealed class LuaCodeActionHandlerTest
{
    private static LuaCodeActionHandler MakeSut()
    {
        return new LuaCodeActionHandler();
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
}