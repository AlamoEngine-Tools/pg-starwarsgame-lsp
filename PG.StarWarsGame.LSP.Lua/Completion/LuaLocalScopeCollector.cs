// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Loretta.CodeAnalysis.Text;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Completion;

internal static class LuaLocalScopeCollector
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    private static readonly IReadOnlySet<string> s_lua51Globals =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pairs", "ipairs", "table", "string", "math", "type", "print", "error", "assert",
            "pcall", "xpcall", "select", "unpack", "next", "rawget", "rawset", "setmetatable",
            "getmetatable", "require", "tostring", "tonumber", "io", "os", "coroutine",
            "_G", "_VERSION", "dofile", "load", "loadfile", "rawequal", "rawlen",
            "collectgarbage", "gcinfo", "newproxy"
        };

    public static IReadOnlyList<ScopeEntry> CollectAt(
        string text,
        int line,
        int character,
        string documentUri,
        GameIndex index,
        ILuaApiSchemaProvider schemaProvider,
        IFileHelper fileHelper)
    {
        var entries = new List<ScopeEntry>();

        // 1. AST-based: locals and parameters visible at cursor
        var tree = LuaSyntaxTree.ParseText(text, s_parseOptions);
        var root = tree.GetRoot();
        var cursorOffset = ComputeOffset(text, line, character);

        CollectLocals(root, cursorOffset, entries);
        CollectParameters(root, cursorOffset, entries);

        // 2. Own file globals
        var normalizedUri = fileHelper.NormalizeUri(documentUri);
        index.Documents.TryGetValue(normalizedUri, out var docIndex);

        if (docIndex is not null)
            foreach (var sym in docIndex.Symbols)
                if (sym.Kind == GameSymbolKind.LuaGlobal)
                    entries.Add(new ScopeEntry(sym.Id, ScopeEntryKind.OwnGlobal, null));

        // 3. Required file globals (transitive closure)
        CollectRequiredGlobals(normalizedUri, docIndex, index, fileHelper, entries);

        // 4. Engine API
        foreach (var name in schemaProvider.AllFunctionNames)
            entries.Add(new ScopeEntry(name, ScopeEntryKind.EngineApi,
                schemaProvider.GetFunctionDescription(name)));

        // 5. Lua 5.1 builtins
        foreach (var name in s_lua51Globals)
            entries.Add(new ScopeEntry(name, ScopeEntryKind.Lua51Builtin, null));

        return entries;
    }

    private static int ComputeOffset(string text, int line, int character)
    {
        var offset = 0;
        var lines = text.Split('\n');
        for (var i = 0; i < line && i < lines.Length; i++)
            offset += lines[i].Length + 1;
        offset += line < lines.Length ? Math.Min(character, lines[line].Length) : 0;
        return offset;
    }

    private static void CollectLocals(
        SyntaxNode root,
        int cursorOffset,
        List<ScopeEntry> entries)
    {
        foreach (var local in root.DescendantNodes().OfType<LocalVariableDeclarationStatementSyntax>())
        {
            // Local must be declared before cursor
            if (local.Span.End > cursorOffset) continue;

            // The local's enclosing function (if any) must also contain the cursor
            var enclosingFuncSpan = GetEnclosingFunctionFullSpan(local);
            if (enclosingFuncSpan.HasValue && !enclosingFuncSpan.Value.Contains(cursorOffset)) continue;

            foreach (var nameDecl in local.Names)
                entries.Add(new ScopeEntry(nameDecl.Name, ScopeEntryKind.LocalVariable, null));
        }
    }

    private static void CollectParameters(
        SyntaxNode root,
        int cursorOffset,
        List<ScopeEntry> entries)
    {
        // Parameters are in scope whenever the cursor is inside the function's full span
        foreach (var funcDecl in root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>())
            if (funcDecl.FullSpan.Contains(cursorOffset))
                AddParameters(funcDecl.Parameters, entries);

        foreach (var localFunc in root.DescendantNodes().OfType<LocalFunctionDeclarationStatementSyntax>())
            if (localFunc.FullSpan.Contains(cursorOffset))
                AddParameters(localFunc.Parameters, entries);

        foreach (var anonFunc in root.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>())
            if (anonFunc.FullSpan.Contains(cursorOffset))
                AddParameters(anonFunc.Parameters, entries);
    }

    private static TextSpan? GetEnclosingFunctionFullSpan(
        SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent is not null)
        {
            if (parent is FunctionDeclarationStatementSyntax or
                LocalFunctionDeclarationStatementSyntax or
                AnonymousFunctionExpressionSyntax)
                return parent.FullSpan;
            parent = parent.Parent;
        }

        return null;
    }

    private static void AddParameters(ParameterListSyntax? paramList, List<ScopeEntry> entries)
    {
        if (paramList is null) return;
        foreach (var p in paramList.Parameters)
            if (p is NamedParameterSyntax np)
                entries.Add(new ScopeEntry(np.Identifier.Text, ScopeEntryKind.Parameter, null));
    }

    private static void CollectRequiredGlobals(
        string normalizedUri,
        DocumentIndex? docIndex,
        GameIndex index,
        IFileHelper fileHelper,
        List<ScopeEntry> entries)
    {
        if (docIndex is null || docIndex.RequireArgs.IsDefaultOrEmpty) return;

        var directRequired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var arg in docIndex.RequireArgs)
        {
            var resolved = LuaRequireResolver.Resolve(arg, index.Documents, fileHelper, normalizedUri);
            if (resolved is not null) directRequired.Add(resolved);
        }

        if (directRequired.Count == 0) return;

        var transitive = LuaTransitiveRequireResolver.GetTransitiveDependencies(
            directRequired, index.Documents, fileHelper);

        foreach (var (id, symbols) in index.WorkspaceDefinitions)
        foreach (var sym in symbols)
        {
            if (sym.Kind != GameSymbolKind.LuaGlobal) continue;
            if (sym.Origin is not FileOrigin fo) continue;
            if (string.Equals(fo.Uri, normalizedUri, StringComparison.OrdinalIgnoreCase)) continue;
            if (!transitive.Contains(fo.Uri)) continue;
            entries.Add(new ScopeEntry(id, ScopeEntryKind.RequiredGlobal, null));
        }
    }
}