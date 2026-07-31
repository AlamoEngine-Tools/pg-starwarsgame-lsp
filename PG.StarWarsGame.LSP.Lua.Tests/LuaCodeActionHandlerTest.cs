// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Core;
using System.IO.Abstractions.TestingHelpers;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Tests;

public sealed class LuaCodeActionHandlerTest
{
    private static LuaCodeActionHandler MakeSut(IGameWorkspaceHost? host = null, IFileHelper? fileHelper = null,
        ILspConfigurationProvider? config = null)
    {
        return new LuaCodeActionHandler(
            host ?? new FakeWorkspaceHost(),
            fileHelper ?? new FileHelper(new MockFileSystem()),
            config ?? new FakeLspConfigurationProvider());
    }

    // ── feature flag ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LuaCodeActionsFlagOff_ReturnsEmpty()
    {
        // Same arrange as Handle_RedundantRequireDiagnostic - only the flag differs.
        var diag = RedundantRequireDiag(3, 0, 12);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Lua = new LuaFeatureFlags { CodeActions = false } });

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

    private static Diagnostic DuplicateRequireDiag(int line, int startChar, int endChar)
    {
        return new Diagnostic
        {
            Code = new DiagnosticCode(DiagnosticIds.LuaDuplicateRequire.ToString()),
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
            Code = new DiagnosticCode(DiagnosticIds.LuaRedundantRequire.ToString()),
            Range = new LspRange(new Position(line, startChar), new Position(line, endChar)),
            Severity = DiagnosticSeverity.Warning,
            Message = "require(\"x\") is redundant.",
            Source = AppProperties.LspServerId
        };
    }

    // ── suppression quick fixes ──────────────────────────────────────────────

    private static (LuaCodeActionHandler Sut, FakeWorkspaceHost Host) SutWithDocument(string text)
    {
        var host = new FakeWorkspaceHost();
        host.Set("file:///s.lua", text);
        return (MakeSut(host), host);
    }

    private static async Task<IReadOnlyList<CodeAction>> SuppressionActions(string text, int line)
    {
        var (sut, _) = SutWithDocument(text);
        var request = ParamsWithDiagnostics("file:///s.lua", RedundantRequireDiag(line, 0, 12));
        var result = await sut.Handle(request, CancellationToken.None);

        return result!
            .Select(a => a.CodeAction!)
            .Where(a => a.Title.StartsWith("Suppress aetswg-", StringComparison.Ordinal))
            .ToList();
    }

    [Fact]
    public async Task Handle_InsideAFunction_OffersAllFourScopes()
    {
        var titles = (await SuppressionActions("function Foo()\n  require(\"x\")\nend\n", 1))
            .Select(a => a.Title).ToList();

        Assert.Equal(4, titles.Count);
        Assert.Contains(titles, t => t.Contains("this line"));
        Assert.Contains(titles, t => t.Contains("this function"));
        Assert.Contains(titles, t => t.Contains("this file"));
        Assert.Contains(titles, t => t.Contains("across the project"));
    }

    // No enclosing function means no function scope to offer - three actions, not a bogus fourth.
    [Fact]
    public async Task Handle_AtFileLevel_OmitsTheFunctionScope()
    {
        var titles = (await SuppressionActions("require(\"x\")\n", 0)).Select(a => a.Title).ToList();

        Assert.Equal(3, titles.Count);
        Assert.DoesNotContain(titles, t => t.Contains("this function"));
    }

    [Fact]
    public async Task Handle_LineScopedAction_InsertsALuaCommentAboveTheDiagnostic()
    {
        var action = (await SuppressionActions("function Foo()\n  require(\"x\")\nend\n", 1))
            .First(a => a.Title.Contains("this line"));
        var edit = action.Edit!.Changes![DocumentUri.From("file:///s.lua")].Single();

        Assert.Equal(1, edit.Range.Start.Line);
        // Indented to match the line it guards, and in Lua's comment syntax rather than XML's.
        Assert.Equal($"  -- aetswg:suppress {DiagnosticIds.LuaRedundantRequire}\n", edit.NewText);
    }

    [Fact]
    public async Task Handle_ProjectScopedAction_IsACommandCarryingTheId()
    {
        var action = (await SuppressionActions("require(\"x\")\n", 0))
            .First(a => a.Title.Contains("across the project"));

        Assert.Null(action.Edit);
        Assert.Equal(DiagnosticIds.LuaRedundantRequire.ToString(),
            action.Command!.Arguments!.Single().ToString());
    }

    // Silencing is never the fix to reach for by reflex.
    [Fact]
    public async Task Handle_NoSuppressionIsMarkedPreferred()
    {
        var actions = await SuppressionActions("function Foo()\n  require(\"x\")\nend\n", 1);

        Assert.DoesNotContain(actions, a => a.IsPreferred);
    }

    // Loretta's parse errors carry ids too, so they get the same scopes as anything else.
    [Fact]
    public async Task Handle_ASyntaxErrorDiagnostic_AlsoOffersSuppression()
    {
        var (sut, _) = SutWithDocument("function Foo(\n");
        var diag = new Diagnostic
        {
            Code = new DiagnosticCode(new DiagnosticId(DiagnosticGroup.Syntax, 1003).ToString()),
            Range = new LspRange(new Position(0, 0), new Position(0, 5)),
            Severity = DiagnosticSeverity.Error,
            Message = "token expected",
            Source = AppProperties.LspServerId
        };

        var result = await sut.Handle(ParamsWithDiagnostics("file:///s.lua", diag), CancellationToken.None);

        Assert.Contains(result!, a => a.CodeAction!.Title.Contains("across the project"));
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
        // Diagnostic is on line 5 - edit must cover (5,0)..(6,0) to delete the full line.
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
            Code = new DiagnosticCode(DiagnosticIds.LuaEngineUpvalue.ToString()),
            Range = new LspRange(new Position(useLine, useChar), new Position(useLine, useEnd)),
            Severity = DiagnosticSeverity.Warning,
            Message = "File-level local 'x' is captured as an upvalue by 'Foo'.",
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
        var diag = EngineUpvalueDiag(2, 11, 12, 0, 1);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut().Handle(request, CancellationToken.None);

        Assert.Contains(result!, a => a.CodeAction?.Title?.Contains("upvalue-ok") == true);
    }

    [Fact]
    public async Task Handle_EngineUpvalueDiagnostic_SuppressAction_InsertsAnnotationAboveDeclaration()
    {
        // local is on line 0 → annotation insert is (0,0)-(0,0) with "---@upvalue-ok\n"
        var diag = EngineUpvalueDiag(2, 11, 12, 0, 1);
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
        var diag = EngineUpvalueDiag(2, 11, 12, 0, 1);
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
        var diag1 = EngineUpvalueDiag(2, 11, 12, 0, 1);
        var diag2 = EngineUpvalueDiag(5, 11, 12, 0, 4);
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
        var diag = EngineUpvalueDiag(2, 11, 12, 0, 1);
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
        var diag = EngineUpvalueDiag(2, 11, 12, 0, 1);
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
        var diag = EngineUpvalueDiag(2, 11, 12, 0, 1);
        var request = ParamsWithDiagnostics("file:///s.lua", diag);

        var result = await MakeSut(host).Handle(request, CancellationToken.None);

        var edits = result!.First(a => a.CodeAction?.Title?.Contains("inside") == true)
            .CodeAction!.Edit!.Changes![DocumentUri.From("file:///s.lua")].ToList();
        Assert.Contains(edits, e =>
            e.Range.Start.Line == 2 && e.Range.Start.Character == 0 &&
            e.Range.End.Line == 2 && e.Range.End.Character == 0 &&
            e.NewText == "    local x = 1\n");
    }

    // ── disk fallback (vscode restore race) ──────────────────────────────────

    [Fact]
    public async Task Handle_EngineUpvalue_FileOnDiskNotInHost_MoveActionProduced()
    {
        // Simulates the vscode-languageclient restored-tab race: file on disk, no didOpen sent yet.
        const string docText = "local x = 1\nfunction Foo()\n    return x\nend";
        var path = Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, "scripts", "s.lua");
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new(docText)
        });
        var fileHelper = new FileHelper(fileSystem);
        var uri = fileHelper.PathToFileUri(path);

        // No document in workspace host
        var diag = EngineUpvalueDiag(2, 11, 12, 0, 1, uri);
        var request = new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Range = new LspRange(new Position(0, 0), new Position(0, 0)),
            Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>(diag) }
        };

        var result = await MakeSut(fileHelper: fileHelper).Handle(request, CancellationToken.None);

        Assert.Contains(result!, a => a.CodeAction?.Title?.Contains("inside") == true);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = new(StringComparer.OrdinalIgnoreCase);

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public IEnumerable<TrackedDocument> All => _docs.Values;

        public void Set(string uri, string text)
        {
            _docs[uri] = new TrackedDocument(uri, text, 1);
        }
    }
}