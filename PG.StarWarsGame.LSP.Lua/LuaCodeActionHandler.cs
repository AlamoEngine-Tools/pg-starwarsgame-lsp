// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaCodeActionHandler : CodeActionHandlerBase
{
    private readonly IFileHelper _fileHelper;
    private readonly IGameWorkspaceHost _workspaceHost;

    public LuaCodeActionHandler(IGameWorkspaceHost workspaceHost, IFileHelper fileHelper)
    {
        _workspaceHost = workspaceHost;
        _fileHelper = fileHelper;
    }

    public override Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri;

        var actions = request.Context.Diagnostics
            .Where(d => d.Code?.IsString == true)
            .SelectMany(d => BuildActionsForDiagnostic(uri, d, request.Context.Diagnostics))
            .Where(a => a is not null)
            .Cast<CommandOrCodeAction>();

        return Task.FromResult<CommandOrCodeActionContainer?>(new CommandOrCodeActionContainer(actions));
    }

    public override Task<CodeAction> Handle(CodeAction request, CancellationToken ct)
    {
        return Task.FromResult(request);
    }

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(
        CodeActionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new CodeActionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("lua"),
            CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.QuickFix),
            ResolveProvider = false
        };
    }

    private IEnumerable<CommandOrCodeAction?> BuildActionsForDiagnostic(
        DocumentUri docUri, Diagnostic d, Container<Diagnostic> allDiagnostics)
    {
        switch (d.Code!.Value.String)
        {
            case LuaDiagnosticCodes.RedundantRequire:
                yield return BuildDeleteLineAction(docUri, d, "Remove redundant require");
                break;
            case LuaDiagnosticCodes.DuplicateRequire:
                yield return BuildDeleteLineAction(docUri, d, "Remove duplicate require");
                break;
            case LuaDiagnosticCodes.EngineUpvalue:
                foreach (var a in BuildUpvalueActions(docUri, d, allDiagnostics))
                    yield return a;
                break;
        }
    }

    private IEnumerable<CommandOrCodeAction> BuildUpvalueActions(
        DocumentUri docUri, Diagnostic d, Container<Diagnostic> allDiagnostics)
    {
        if (d.RelatedInformation is null || d.RelatedInformation.Count() < 2)
            yield break;

        var relInfoList = d.RelatedInformation.ToList();
        var localEntry = relInfoList.FirstOrDefault(r =>
            r.Message.Contains("local", StringComparison.OrdinalIgnoreCase));
        var funcEntry = relInfoList.FirstOrDefault(r =>
            r.Message.Contains("function", StringComparison.OrdinalIgnoreCase));

        if (localEntry is null || funcEntry is null)
            yield break;

        var declLine = localEntry.Location.Range.Start.Line;
        var funcDeclLine = funcEntry.Location.Range.Start.Line;

        // "Suppress with ---@upvalue-ok" — always available
        yield return BuildSuppressUpvalueAction(docUri, d, declLine);

        // "Move local inside function" — only when exactly one function captures this local
        var sameDeclCount = allDiagnostics.Count(other =>
            other.Code?.String == LuaDiagnosticCodes.EngineUpvalue &&
            other.RelatedInformation is not null &&
            other.RelatedInformation.Any(r =>
                r.Message.Contains("local", StringComparison.OrdinalIgnoreCase) &&
                r.Location.Range.Start.Line == declLine));

        if (sameDeclCount == 1)
        {
            var moveAction = BuildMoveLocalIntoFunctionAction(docUri, d, declLine, funcDeclLine);
            if (moveAction is not null)
                yield return moveAction;
        }
    }

    private static CommandOrCodeAction BuildSuppressUpvalueAction(
        DocumentUri docUri, Diagnostic d, int declLine)
    {
        var insertRange = new LspRange(new Position(declLine, 0), new Position(declLine, 0));

        return new CommandOrCodeAction(new CodeAction
        {
            Title = "Suppress with ---@upvalue-ok",
            Kind = CodeActionKind.QuickFix,
            Diagnostics = new Container<Diagnostic>(d),
            IsPreferred = false,
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [docUri] = [new TextEdit { Range = insertRange, NewText = "---@upvalue-ok\n" }]
                }
            }
        });
    }

    private CommandOrCodeAction? BuildMoveLocalIntoFunctionAction(
        DocumentUri docUri, Diagnostic d, int declLine, int funcDeclLine)
    {
        var uriString = _fileHelper.NormalizeUri(docUri.ToString());
        if (!_workspaceHost.TryGetOrReadFromDisk(_fileHelper, uriString, out var doc))
            return null;

        var lines = doc.Text.Split('\n');
        if (declLine >= lines.Length)
            return null;

        var declText = lines[declLine].TrimEnd('\r').TrimStart();
        var funcBodyLine = funcDeclLine + 1;

        var deleteRange = new LspRange(new Position(declLine, 0), new Position(declLine + 1, 0));
        var insertRange = new LspRange(new Position(funcBodyLine, 0), new Position(funcBodyLine, 0));

        return new CommandOrCodeAction(new CodeAction
        {
            Title = "Move local inside function",
            Kind = CodeActionKind.QuickFix,
            Diagnostics = new Container<Diagnostic>(d),
            IsPreferred = true,
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [docUri] =
                    [
                        new TextEdit { Range = deleteRange, NewText = "" },
                        new TextEdit { Range = insertRange, NewText = "    " + declText + "\n" }
                    ]
                }
            }
        });
    }

    private static CodeAction BuildDeleteLineAction(DocumentUri docUri, Diagnostic d, string title)
    {
        var line = d.Range.Start.Line;
        var deleteRange = new LspRange(new Position(line, 0), new Position(line + 1, 0));

        return new CodeAction
        {
            Title = title,
            Kind = CodeActionKind.QuickFix,
            Diagnostics = new Container<Diagnostic>(d),
            IsPreferred = true,
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [docUri] = [new TextEdit { Range = deleteRange, NewText = "" }]
                }
            }
        };
    }
}