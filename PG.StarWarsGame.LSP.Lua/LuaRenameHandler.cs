// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using LorLocation = Loretta.CodeAnalysis.Location;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaRenameHandler : RenameHandlerBase
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<LuaRenameHandler> _logger;
    private readonly IGameWorkspaceHost _workspaceHost;

    public LuaRenameHandler(
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        ILogger<LuaRenameHandler> logger)
    {
        _indexService = indexService;
        _workspaceHost = workspaceHost;
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<WorkspaceEdit?>(null);

        if (!_workspaceHost.TryGet(uri, out var doc))
            return Task.FromResult<WorkspaceEdit?>(null);

        var index = _indexService.Current;
        var globalName = FindGlobalAtCursor(doc.Text, request.Position.Line, request.Position.Character, index);
        if (globalName is null)
            return Task.FromResult<WorkspaceEdit?>(null);

        var newName = request.NewName;
        var changes = new Dictionary<DocumentUri, List<TextEdit>>();

        AddDefinitionEdits(globalName, newName, index, changes);
        AddReferenceEdits(globalName, newName, index, changes);

        if (changes.Count == 0)
            return Task.FromResult<WorkspaceEdit?>(null);

        _logger.LogDebug("Lua rename {Name} → {NewName}: {Count} file(s)", globalName, newName, changes.Count);
        return Task.FromResult<WorkspaceEdit?>(new WorkspaceEdit
        {
            Changes = changes.ToDictionary(kvp => kvp.Key, kvp => (IEnumerable<TextEdit>)kvp.Value)
        });
    }

    private static string? FindGlobalAtCursor(string text, int line, int character, GameIndex index)
    {
        var tree = LuaSyntaxTree.ParseText(text, s_parseOptions);
        var root = tree.GetRoot();

        // Check call sites — IdentifierNameSyntax is used for identifier expressions.
        foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (!LocationContainsPosition(id.GetLocation(), line, character)) continue;
            if (IsKnownLuaGlobal(id.Name, index)) return id.Name;
        }

        // Check function declarations — the name token lives in SimpleFunctionNameSyntax,
        // which is NOT an IdentifierNameSyntax, so must be checked separately.
        foreach (var funcDecl in root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>())
        {
            if (funcDecl.Name is not SimpleFunctionNameSyntax simpleName) continue;
            if (!LocationContainsPosition(simpleName.Name.GetLocation(), line, character)) continue;
            var name = simpleName.Name.Text;
            if (IsKnownLuaGlobal(name, index)) return name;
        }

        return null;
    }

    private static bool IsKnownLuaGlobal(string name, GameIndex index)
    {
        return index.WorkspaceDefinitions.TryGetValue(name, out var defs) &&
               defs.Any(s => s.Kind == GameSymbolKind.LuaGlobal);
    }

    private static bool LocationContainsPosition(LorLocation location, int line, int character)
    {
        var span = location.GetLineSpan();
        var start = span.StartLinePosition;
        var end = span.EndLinePosition;
        if (line < start.Line || line > end.Line) return false;
        if (line == start.Line && character < start.Character) return false;
        if (line == end.Line && character > end.Character) return false;
        return true;
    }

    private void AddDefinitionEdits(
        string globalName, string newName, GameIndex index,
        Dictionary<DocumentUri, List<TextEdit>> changes)
    {
        if (!index.WorkspaceDefinitions.TryGetValue(globalName, out var defs)) return;

        foreach (var sym in defs)
        {
            if (sym.Kind != GameSymbolKind.LuaGlobal) continue;
            if (sym.Origin is not FileOrigin fo) continue;

            var text = GetText(fo.Uri);
            if (text is null) continue;

            var range = FindFunctionNameRange(text, globalName);
            if (range is null) continue;

            AddEdit(changes, fo.Uri, new TextEdit { NewText = newName, Range = range });
        }
    }

    private void AddReferenceEdits(
        string globalName, string newName, GameIndex index,
        Dictionary<DocumentUri, List<TextEdit>> changes)
    {
        foreach (var docUri in index.Documents.Keys)
        {
            if (!docUri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) continue;

            var text = GetText(docUri);
            if (text is null) continue;

            var tree = LuaSyntaxTree.ParseText(text, s_parseOptions);
            var root = tree.GetRoot();

            foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (!string.Equals(id.Name, globalName, StringComparison.OrdinalIgnoreCase)) continue;

                var span = id.GetLocation().GetLineSpan();
                var start = span.StartLinePosition;
                var end = span.EndLinePosition;
                AddEdit(changes, docUri, new TextEdit
                {
                    NewText = newName,
                    Range = new LspRange(
                        new Position(start.Line, start.Character),
                        new Position(end.Line, end.Character))
                });
            }
        }
    }

    private static LspRange? FindFunctionNameRange(string text, string globalName)
    {
        var tree = LuaSyntaxTree.ParseText(text, s_parseOptions);
        var root = tree.GetRoot();

        foreach (var funcDecl in root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>())
        {
            if (funcDecl.Name is not SimpleFunctionNameSyntax simpleName) continue;
            if (!string.Equals(simpleName.Name.Text, globalName, StringComparison.OrdinalIgnoreCase)) continue;

            var span = simpleName.Name.GetLocation().GetLineSpan();
            var start = span.StartLinePosition;
            var end = span.EndLinePosition;
            return new LspRange(
                new Position(start.Line, start.Character),
                new Position(end.Line, end.Character));
        }

        return null;
    }

    private string? GetText(string uri)
    {
        if (_workspaceHost.TryGet(uri, out var doc))
            return doc.Text;

        var path = _fileHelper.FileUriToPath(uri);
        if (path is null) return null;

        try
        {
            return _fileHelper.FileSystem.File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    private static void AddEdit(Dictionary<DocumentUri, List<TextEdit>> changes, string uri, TextEdit edit)
    {
        var key = DocumentUri.From(uri);
        if (!changes.TryGetValue(key, out var list))
            changes[key] = list = [];
        list.Add(edit);
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability, ClientCapabilities clientCapabilities)
    {
        return new RenameRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("lua") };
    }
}