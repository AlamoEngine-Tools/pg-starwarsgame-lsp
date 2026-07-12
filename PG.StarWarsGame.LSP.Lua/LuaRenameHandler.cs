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
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Rename;
using PG.StarWarsGame.LSP.Lua.Util;
using LorLocation = Loretta.CodeAnalysis.Location;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaRenameHandler : ILuaRenameProvider
{
    private readonly ILogger<LuaRenameHandler> _logger;
    private readonly ILuaParseCache _parseCache;
    private readonly ISchemaProvider _schema;
    private readonly IDocumentTextSource _textSource;

    // textSource stays alongside the parse cache: XmlObjectRenameBuilder edits XML documents,
    // which are outside the Lua parse cache's world.
    public LuaRenameHandler(
        ILuaParseCache parseCache,
        IDocumentTextSource textSource,
        ISchemaProvider schema,
        ILogger<LuaRenameHandler> logger)
    {
        _parseCache = parseCache;
        _textSource = textSource;
        _schema = schema;
        _logger = logger;
    }

    public WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index)
    {
        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null) return null;

        // Try game-object reference first (cursor on a string literal whose value is a known XmlObject).
        var xmlObjectId = FindXmlObjectAtCursor(parsed.Tree, request.Position.Line, request.Position.Character, index);
        if (xmlObjectId is not null)
        {
            if (StoryRenameGuard.IsStorySymbol(xmlObjectId, index) &&
                StoryRenameGuard.Check(xmlObjectId, request.NewName, index) is { } objection)
                throw new InvalidOperationException(objection);
            return XmlObjectRenameBuilder.Build(xmlObjectId, request.NewName, index, _schema, _textSource, _logger);
        }

        // Fall back to Lua global (cursor on an identifier that is a known LuaGlobal).
        var luaGlobalId = FindLuaGlobalAtCursor(parsed.Tree, request.Position.Line, request.Position.Character,
            index);
        if (luaGlobalId is not null)
            return LuaGlobalRenameBuilder.Build(luaGlobalId, request.NewName, index, _parseCache, _logger);

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
                if (!index.IsLeafOwned(hit.Value.Id)) return null;

                var range = hit.Value.Range;
                // Zero-length means cursor is on a declaration; extend by the symbol name length.
                if (range.Start == range.End)
                    range = new LspRange(range.Start,
                        new Position(range.Start.Line, range.Start.Character + hit.Value.Id.Length));

                return new RangeOrPlaceholderRange(range);
            }
        }

        // XmlObject string literal path — requires the parsed document.
        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null) return null;

        return FindXmlObjectStringRange(parsed.Tree, line, character, index);
    }

    private RangeOrPlaceholderRange? FindXmlObjectStringRange(
        SyntaxTree tree, int line, int character, GameIndex index)
    {
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
            if (!index.IsLeafOwned(value)) return null;

            return new RangeOrPlaceholderRange(new LspRange(
                new Position(line, innerStart),
                new Position(line, innerEnd)));
        }

        return null;
    }

    private static string? FindXmlObjectAtCursor(SyntaxTree tree, int line, int character, GameIndex index)
    {
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

    private static string? FindLuaGlobalAtCursor(SyntaxTree tree, int line, int character, GameIndex index)
    {
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

}