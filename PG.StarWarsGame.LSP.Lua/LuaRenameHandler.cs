// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Rename;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Rename;
using PG.StarWarsGame.LSP.Lua.Util;
using LorLocation = Loretta.CodeAnalysis.Location;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaRenameHandler : ILuaRenameProvider
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    private readonly IFileHelper _fileHelper;
    private readonly ILogger<LuaRenameHandler> _logger;
    private readonly ISchemaProvider _schema;
    private readonly IGameWorkspaceHost _workspaceHost;

    public LuaRenameHandler(
        IGameWorkspaceHost workspaceHost,
        ISchemaProvider schema,
        IFileHelper fileHelper,
        ILogger<LuaRenameHandler> logger)
    {
        _workspaceHost = workspaceHost;
        _schema = schema;
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index)
    {
        var text = GetText(uri);
        if (text is null) return null;

        // Try game-object reference first (cursor on a string literal whose value is a known XmlObject).
        var xmlObjectId = FindXmlObjectAtCursor(text, request.Position.Line, request.Position.Character, index);
        if (xmlObjectId is not null)
            return XmlObjectRenameBuilder.Build(xmlObjectId, request.NewName, index, _schema, _workspaceHost, _fileHelper, _logger);

        // Fall back to Lua global (cursor on an identifier that is a known LuaGlobal).
        var luaGlobalId = FindLuaGlobalAtCursor(text, request.Position.Line, request.Position.Character, index);
        if (luaGlobalId is not null)
            return LuaGlobalRenameBuilder.Build(luaGlobalId, request.NewName, index, _workspaceHost, _fileHelper, _logger);

        return null;
    }

    public RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index)
    {
        // LuaGlobal path — requires document in the index.
        if (index.Documents.TryGetValue(uri, out var docIndex))
        {
            var hit = LuaPositionResolver.FindAtPosition(docIndex, line, character);
            if (hit is not null)
            {
                if (IsBlockedByArchiveOrigin(hit.Value.Id, index)) return null;

                var range = hit.Value.Range;
                // Zero-length means cursor is on a declaration; extend by the symbol name length.
                if (range.Start == range.End)
                    range = new LspRange(range.Start,
                        new Position(range.Start.Line, range.Start.Character + hit.Value.Id.Length));

                return new RangeOrPlaceholderRange(range);
            }
        }

        // XmlObject string literal path — requires document text.
        var text = GetText(uri);
        if (text is null) return null;

        return FindXmlObjectStringRange(text, line, character, index);
    }

    private RangeOrPlaceholderRange? FindXmlObjectStringRange(
        string text, int line, int character, GameIndex index)
    {
        var tree = LuaSyntaxTree.ParseText(text, s_parseOptions);
        var root = tree.GetRoot();

        foreach (var lit in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) continue;

            var loc = lit.GetLocation().GetLineSpan();
            var startLine = loc.StartLinePosition.Line;
            var startChar = loc.StartLinePosition.Character;

            if (startLine != line) continue;

            var value = lit.Token.ValueText;
            var innerStart = startChar + 1;
            var innerEnd = innerStart + value.Length;

            if (character < innerStart || character > innerEnd) continue;

            if (!IsKnownXmlObject(value, index)) continue;
            if (IsBlockedByArchiveOrigin(value, index)) return null;

            return new RangeOrPlaceholderRange(new LspRange(
                new Position(line, innerStart),
                new Position(line, innerEnd)));
        }

        return null;
    }

    private static string? FindXmlObjectAtCursor(string text, int line, int character, GameIndex index)
    {
        var tree = LuaSyntaxTree.ParseText(text, s_parseOptions);
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

    private static string? FindLuaGlobalAtCursor(string text, int line, int character, GameIndex index)
    {
        var tree = LuaSyntaxTree.ParseText(text, s_parseOptions);
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

    private static bool IsBlockedByArchiveOrigin(string id, GameIndex index)
    {
        return index.WorkspaceDefinitions.TryGetValue(id, out var defs) &&
               defs.Any(s => s.Origin is not FileOrigin);
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
}
