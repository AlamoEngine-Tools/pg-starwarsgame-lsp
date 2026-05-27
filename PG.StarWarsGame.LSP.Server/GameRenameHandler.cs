// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml;
using LorLocation = Loretta.CodeAnalysis.Location;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Server;

public sealed class GameRenameHandler : RenameHandlerBase
{
    private static readonly LuaParseOptions s_luaParseOptions = new(LuaSyntaxOptions.Lua51);

    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<GameRenameHandler> _logger;
    private readonly ISchemaProvider _schema;
    private readonly IGameWorkspaceHost _workspaceHost;

    public GameRenameHandler(
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        ISchemaProvider schema,
        IEaWXmlContext eaWXmlContext,
        IFileHelper fileHelper,
        ILogger<GameRenameHandler> logger)
    {
        _indexService = indexService;
        _workspaceHost = workspaceHost;
        _schema = schema;
        _eaWXmlContext = eaWXmlContext;
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        var index = _indexService.Current;

        if (uri.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return HandleXml(uri, request, index);
        if (uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return HandleLua(uri, request, index);

        return Task.FromResult<WorkspaceEdit?>(null);
    }

    // ── XML path ───────────────────────────────────────────────────────────────

    private Task<WorkspaceEdit?> HandleXml(string uri, RenameParams request, GameIndex index)
    {
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult<WorkspaceEdit?>(null);

        if (!index.Documents.TryGetValue(uri, out var docIndex))
            return Task.FromResult<WorkspaceEdit?>(null);

        var hit = XmlPositionResolver.FindAtPosition(
            docIndex, request.Position.Line, request.Position.Character);
        if (hit is null)
            return Task.FromResult<WorkspaceEdit?>(null);

        return Task.FromResult(BuildXmlObjectEdit(hit.Value.Id, request.NewName, index));
    }

    // ── Lua path ───────────────────────────────────────────────────────────────

    private Task<WorkspaceEdit?> HandleLua(string uri, RenameParams request, GameIndex index)
    {
        var text = GetText(uri);
        if (text is null)
            return Task.FromResult<WorkspaceEdit?>(null);

        // Try game-object reference first (cursor on a string literal whose value is a known XmlObject).
        var xmlObjectId = FindXmlObjectAtCursor(
            text, request.Position.Line, request.Position.Character, index);
        if (xmlObjectId is not null)
            return Task.FromResult(BuildXmlObjectEdit(xmlObjectId, request.NewName, index));

        // Fall back to Lua global (cursor on an identifier that is a known LuaGlobal).
        var luaGlobalId = FindLuaGlobalAtCursor(
            text, request.Position.Line, request.Position.Character, index);
        if (luaGlobalId is not null)
            return Task.FromResult(BuildLuaGlobalEdit(luaGlobalId, request.NewName, index));

        return Task.FromResult<WorkspaceEdit?>(null);
    }

    // ── XmlObject edit builder ─────────────────────────────────────────────────

    private WorkspaceEdit? BuildXmlObjectEdit(string id, string newName, GameIndex index)
    {
        // Block rename if any definition is not workspace-owned.
        if (index.WorkspaceDefinitions.TryGetValue(id, out var defs))
            if (defs.Any(s => s.Origin is not FileOrigin))
            {
                _logger.LogDebug("Rename blocked: {Id} has non-FileOrigin definition", id);
                return null;
            }

        var changes = new Dictionary<DocumentUri, List<TextEdit>>();

        // XML definition edits — locate the name-attribute value in the definition file.
        if (index.WorkspaceDefinitions.TryGetValue(id, out defs))
            foreach (var sym in defs)
            {
                if (sym.Origin is not FileOrigin fo) continue;
                var nameTag = sym.TypeName is not null
                    ? _schema.GetObjectType(sym.TypeName)?.NameTag
                    : null;
                if (nameTag is null) continue;
                var defRange = FindNameAttributeRange(fo.Uri, fo.Line, nameTag, id);
                if (defRange is null) continue;
                AddEdit(changes, fo.Uri, new TextEdit { NewText = newName, Range = defRange });
            }

        // Reference edits — use precise positions from the index (covers both XML and Lua).
        if (index.WorkspaceReferences.TryGetValue(id, out var refs))
            foreach (var r in refs)
                AddEdit(changes, r.DocumentUri, new TextEdit
                {
                    NewText = newName,
                    Range = new LspRange(
                        new Position(r.Line, r.Column),
                        new Position(r.Line, r.Column + r.Length))
                });

        if (changes.Count == 0)
            return null;

        _logger.LogDebug("Rename XmlObject {Id} → {NewName}: {Count} file(s)", id, newName, changes.Count);
        return new WorkspaceEdit
        {
            Changes = changes.ToDictionary(kvp => kvp.Key, kvp => (IEnumerable<TextEdit>)kvp.Value)
        };
    }

    // ── LuaGlobal edit builder ─────────────────────────────────────────────────

    private WorkspaceEdit? BuildLuaGlobalEdit(string id, string newName, GameIndex index)
    {
        var changes = new Dictionary<DocumentUri, List<TextEdit>>();

        // Definition edits — find `function <id>(` in the definition file.
        if (index.WorkspaceDefinitions.TryGetValue(id, out var defs))
            foreach (var sym in defs)
            {
                if (sym.Kind != GameSymbolKind.LuaGlobal) continue;
                if (sym.Origin is not FileOrigin fo) continue;
                var text = GetText(fo.Uri);
                if (text is null) continue;
                var range = FindFunctionNameRange(text, id);
                if (range is null) continue;
                AddEdit(changes, fo.Uri, new TextEdit { NewText = newName, Range = range });
            }

        // Reference edits — use indexed LuaGlobal refs (O(1) lookup, no per-file re-parse).
        if (index.WorkspaceReferences.TryGetValue(id, out var refs))
            foreach (var r in refs)
            {
                if (r.ExpectedKind != GameSymbolKind.LuaGlobal) continue;
                AddEdit(changes, r.DocumentUri, new TextEdit
                {
                    NewText = newName,
                    Range = new LspRange(
                        new Position(r.Line, r.Column),
                        new Position(r.Line, r.Column + r.Length))
                });
            }

        if (changes.Count == 0)
            return null;

        _logger.LogDebug("Rename LuaGlobal {Id} → {NewName}: {Count} file(s)", id, newName, changes.Count);
        return new WorkspaceEdit
        {
            Changes = changes.ToDictionary(kvp => kvp.Key, kvp => (IEnumerable<TextEdit>)kvp.Value)
        };
    }

    // ── cursor detection ───────────────────────────────────────────────────────

    private static string? FindXmlObjectAtCursor(
        string text, int line, int character, GameIndex index)
    {
        var tree = LuaSyntaxTree.ParseText(text, s_luaParseOptions);
        var root = tree.GetRoot();

        foreach (var lit in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) continue;
            if (!LocationContainsPosition(lit.GetLocation(), line, character)) continue;

            var value = lit.Token.ValueText;
            if (IsKnownXmlObject(value, index)) return value;
        }

        return null;
    }

    private static string? FindLuaGlobalAtCursor(
        string text, int line, int character, GameIndex index)
    {
        var tree = LuaSyntaxTree.ParseText(text, s_luaParseOptions);
        var root = tree.GetRoot();

        foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (!LocationContainsPosition(id.GetLocation(), line, character)) continue;
            if (IsKnownLuaGlobal(id.Name, index)) return id.Name;
        }

        foreach (var funcDecl in root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>())
        {
            if (funcDecl.Name is not SimpleFunctionNameSyntax simpleName) continue;
            if (!LocationContainsPosition(simpleName.Name.GetLocation(), line, character)) continue;
            var name = simpleName.Name.Text;
            if (IsKnownLuaGlobal(name, index)) return name;
        }

        return null;
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static bool IsKnownXmlObject(string name, GameIndex index)
    {
        return index.WorkspaceDefinitions.TryGetValue(name, out var defs) &&
               defs.Any(s => s.Kind == GameSymbolKind.XmlObject);
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

    private LspRange? FindNameAttributeRange(string uri, int line, string nameTag, string currentValue)
    {
        string text;
        if (_workspaceHost.TryGet(uri, out var doc))
        {
            text = doc.Text;
        }
        else
        {
            var path = _fileHelper.FileUriToPath(uri);
            if (path is null) return null;
            try { text = _fileHelper.FileSystem.File.ReadAllText(path); }
            catch { return null; }
        }

        var lines = text.Split('\n');
        if (line >= lines.Length) return null;
        var lineText = lines[line].TrimEnd('\r');

        foreach (var quote in new[] { '"', '\'' })
        {
            var pattern = $"{nameTag}={quote}{currentValue}{quote}";
            var idx = lineText.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) continue;

            var valueStart = idx + nameTag.Length + 2; // +2 for '=' and opening quote
            return new LspRange(
                new Position(line, valueStart),
                new Position(line, valueStart + currentValue.Length));
        }

        return null;
    }

    private static LspRange? FindFunctionNameRange(string text, string globalName)
    {
        var tree = LuaSyntaxTree.ParseText(text, s_luaParseOptions);
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
        try { return _fileHelper.FileSystem.File.ReadAllText(path); }
        catch { return null; }
    }

    private static void AddEdit(
        Dictionary<DocumentUri, List<TextEdit>> changes, string uri, TextEdit edit)
    {
        var key = DocumentUri.From(uri);
        if (!changes.TryGetValue(key, out var list))
            changes[key] = list = [];
        list.Add(edit);
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability, ClientCapabilities clientCapabilities)
    {
        return new RenameRegistrationOptions
        {
            DocumentSelector = new TextDocumentSelector(
                new TextDocumentFilter { Language = "xml" },
                new TextDocumentFilter { Language = "lua" }),
            PrepareProvider = true
        };
    }
}
