// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Lua.Schema;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

internal static class LuaGlobalScopeAnalyzer
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    // Standard Lua 5.1 globals — never flag these as missing requires.
    private static readonly HashSet<string> s_lua51Globals = new(StringComparer.OrdinalIgnoreCase)
    {
        "pairs", "ipairs", "table", "string", "math", "type", "print", "error", "assert",
        "pcall", "xpcall", "select", "unpack", "next", "rawget", "rawset", "setmetatable",
        "getmetatable", "require", "tostring", "tonumber", "io", "os", "coroutine",
        "_G", "_VERSION", "dofile", "load", "loadfile", "rawequal", "rawlen",
        "collectgarbage", "gcinfo", "newproxy"
    };

    public static IReadOnlyList<LspDiagnostic> Analyze(
        string documentUri, string text, GameIndex index, ILuaApiSchemaProvider schemaProvider)
    {
        var tree = LuaSyntaxTree.ParseText(text, s_parseOptions);
        var root = tree.GetRoot();
        var workspaceUris = index.Documents.Keys;
        var diagnostics = new List<LspDiagnostic>();

        // Phase 1: collect resolved require calls.
        var requireCalls = CollectRequireCalls(root, workspaceUris);
        var requiredUris = new HashSet<string>(
            requireCalls.Where(r => r.ResolvedUri is not null).Select(r => r.ResolvedUri!),
            StringComparer.OrdinalIgnoreCase);

        // Phase 2: collect all identifier names used in this file.
        var usedIdentifiers = CollectUsedIdentifiers(root);

        // Phase 3: missing-require diagnostics.
        EmitMissingRequireDiagnostics(
            root, documentUri, index, schemaProvider, requiredUris, diagnostics);

        // Phase 4: unused-require diagnostics.
        EmitUnusedRequireDiagnostics(
            requireCalls, usedIdentifiers, index, diagnostics);

        return diagnostics;
    }

    // ── phase 1 ───────────────────────────────────────────────────────────────

    private sealed record RequireCall(string Arg, string? ResolvedUri, FunctionCallExpressionSyntax Node);

    private static List<RequireCall> CollectRequireCalls(
        SyntaxNode root, IEnumerable<string> workspaceUris)
    {
        var uriList = workspaceUris as ICollection<string> ?? workspaceUris.ToList();
        var calls = new List<RequireCall>();

        foreach (var call in root.DescendantNodes().OfType<FunctionCallExpressionSyntax>())
        {
            if (call.Expression is not IdentifierNameSyntax { Name: "require" }) continue;

            var arg = ExtractStringArg(call);
            if (arg is null) continue;

            if (LuaRequireResolver.IsRelative(arg)) continue;

            var resolved = LuaRequireResolver.Resolve(arg, uriList);
            calls.Add(new RequireCall(arg, resolved, call));
        }

        return calls;
    }

    // ── phase 2 ───────────────────────────────────────────────────────────────

    private static HashSet<string> CollectUsedIdentifiers(SyntaxNode root)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            names.Add(id.Name);
        return names;
    }

    // ── phase 3: missing require ──────────────────────────────────────────────

    private static void EmitMissingRequireDiagnostics(
        SyntaxNode root,
        string documentUri,
        GameIndex index,
        ILuaApiSchemaProvider schemaProvider,
        HashSet<string> requiredUris,
        List<LspDiagnostic> diagnostics)
    {
        // Build a map from LuaGlobal name → defining URI (for globals in other files).
        var luaGlobalsByName = BuildLuaGlobalsMap(index, documentUri);

        // Track names already warned about to deduplicate across occurrences.
        var warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var name = id.Name;

            if (s_lua51Globals.Contains(name)) continue;
            if (schemaProvider.AllFunctionNames.Contains(name)) continue;
            if (!luaGlobalsByName.TryGetValue(name, out var defUri)) continue;
            if (requiredUris.Contains(defUri)) continue;

            if (!warned.Add(name)) continue;

            var span = id.GetLocation().GetLineSpan();
            var start = span.StartLinePosition;
            var end = span.EndLinePosition;
            var filename = Path.GetFileNameWithoutExtension(defUri);

            diagnostics.Add(new LspDiagnostic
            {
                Severity = LspDiagnosticSeverity.Warning,
                Message = $"Global '{name}' is defined in '{filename}' which is not required.",
                Range = new LspRange(
                    new LspPosition(start.Line, start.Character),
                    new LspPosition(end.Line, end.Character)),
                Source = AppProperties.LspServerId
            });
        }
    }

    // Returns a map: LuaGlobal name → defining file URI (only globals NOT defined in documentUri).
    private static Dictionary<string, string> BuildLuaGlobalsMap(GameIndex index, string documentUri)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, symbols) in index.WorkspaceDefinitions)
        {
            foreach (var sym in symbols)
            {
                if (sym.Kind != GameSymbolKind.LuaGlobal) continue;
                if (sym.Origin is not FileOrigin fo) continue;
                if (string.Equals(fo.Uri, documentUri, StringComparison.OrdinalIgnoreCase)) continue;

                // Last writer wins if the same name is defined in multiple files;
                // the caller checks if ANY defining URI is required, so this is fine.
                if (!map.ContainsKey(id))
                    map[id] = fo.Uri;
            }
        }

        return map;
    }

    // ── phase 4: unused require ───────────────────────────────────────────────

    private static void EmitUnusedRequireDiagnostics(
        List<RequireCall> requireCalls,
        HashSet<string> usedIdentifiers,
        GameIndex index,
        List<LspDiagnostic> diagnostics)
    {
        foreach (var (arg, resolvedUri, callNode) in requireCalls)
        {
            if (resolvedUri is null) continue;

            // Get the exported LuaGlobal names from the required file.
            IEnumerable<string> exportedNames;
            if (index.Documents.TryGetValue(resolvedUri, out var requiredDoc))
                exportedNames = requiredDoc.Symbols
                    .Where(s => s.Kind == GameSymbolKind.LuaGlobal)
                    .Select(s => s.Id);
            else
                exportedNames = [];

            // If any exported global is referenced, the require is used.
            if (exportedNames.Any(n => usedIdentifiers.Contains(n)))
                continue;

            var span = callNode.GetLocation().GetLineSpan();
            var start = span.StartLinePosition;
            var end = span.EndLinePosition;

            diagnostics.Add(new LspDiagnostic
            {
                Severity = LspDiagnosticSeverity.Hint,
                Message = $"require(\"{arg}\") is unused — no exported globals are referenced.",
                Range = new LspRange(
                    new LspPosition(start.Line, start.Character),
                    new LspPosition(end.Line, end.Character)),
                Source = AppProperties.LspServerId
            });
        }
    }

    // ── utilities ─────────────────────────────────────────────────────────────

    private static string? ExtractStringArg(FunctionCallExpressionSyntax call)
    {
        if (call.Argument is StringFunctionArgumentSyntax strArg)
            return strArg.Expression.Token.ValueText;

        if (call.Argument is ExpressionListFunctionArgumentSyntax exprList &&
            exprList.Expressions.FirstOrDefault() is LiteralExpressionSyntax lit)
            return lit.Token.ValueText;

        return null;
    }
}
