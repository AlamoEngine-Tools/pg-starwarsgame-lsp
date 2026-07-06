// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Loretta.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using PG.StarWarsGame.LSP.Lua.Parsing;

namespace PG.StarWarsGame.LSP.Lua.Rename;

public static class LuaGlobalRenameBuilder
{
    public static WorkspaceEdit? Build(
        string id, string newName, GameIndex index,
        ILuaParseCache parseCache, ILogger logger)
    {
        if (!index.IsLeafOwned(id))
        {
            logger.LogDebug("Rename blocked: {Id} is not exclusively defined in the leaf layer", id);
            return null;
        }

        var changes = new Dictionary<DocumentUri, List<TextEdit>>();

        // Definition edits — find `function <id>(` in the definition file.
        if (index.WorkspaceDefinitions.TryGetValue(id, out var defs))
            foreach (var sym in defs)
            {
                if (sym.Kind != GameSymbolKind.LuaGlobal) continue;
                if (sym.Origin is not FileOrigin fo) continue;
                var parsed = parseCache.GetOrParse(fo.Uri);
                if (parsed is null) continue;
                var range = FindFunctionNameRange(parsed.Tree, id);
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

        logger.LogDebug("Rename LuaGlobal {Id} → {NewName}: {Count} file(s)", id, newName, changes.Count);
        return new WorkspaceEdit
        {
            Changes = changes.ToDictionary(kvp => kvp.Key, kvp => (IEnumerable<TextEdit>)kvp.Value)
        };
    }

    internal static LspRange? FindFunctionNameRange(SyntaxTree tree, string globalName)
    {
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

    private static void AddEdit(
        Dictionary<DocumentUri, List<TextEdit>> changes, string uri, TextEdit edit)
    {
        var key = DocumentUri.From(uri);
        if (!changes.TryGetValue(key, out var list))
            changes[key] = list = [];
        list.Add(edit);
    }
}