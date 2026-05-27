// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Schema;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticCode = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticCode;
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
        string documentUri, string text, GameIndex index, ILuaApiSchemaProvider schemaProvider,
        IFileHelper fileHelper)
    {
        var tree = LuaSyntaxTree.ParseText(text, s_parseOptions);
        var root = tree.GetRoot();
        var workspaceUris = index.Documents.Keys;
        var diagnostics = new List<LspDiagnostic>();

        // Phase 1: collect resolved require calls.
        var requireCalls = CollectRequireCalls(root, workspaceUris, fileHelper);
        var requiredUris = new HashSet<string>(
            requireCalls.Where(r => r.ResolvedUri is not null).Select(r => r.ResolvedUri!),
            StringComparer.OrdinalIgnoreCase);

        // Pre-compute names that are in scope without any require (own globals + local AST names).
        var locallyBound = CollectLocallyBoundNames(root, index, documentUri);

        // Compute the transitive closure of required URIs.
        var transitiveRequiredUris = LuaTransitiveRequireResolver.GetTransitiveDependencies(
            requiredUris, index.Documents, fileHelper);

        // Phase 2: collect all identifier names used in this file.
        var usedIdentifiers = CollectUsedIdentifiers(root);

        // Phase 3: missing-require diagnostics (uses transitive set + locallyBound skip).
        EmitMissingRequireDiagnostics(
            root, documentUri, index, schemaProvider,
            locallyBound, transitiveRequiredUris, diagnostics);

        // Phase 4: unused-require diagnostics (transitive scope per required file).
        EmitUnusedRequireDiagnostics(
            requireCalls, usedIdentifiers, index, fileHelper, diagnostics);

        // Phase 5: cyclic-require diagnostics.
        EmitCyclicRequireDiagnostics(documentUri, requireCalls, index, fileHelper, diagnostics);

        // Phase 6: global override diagnostics.
        EmitGlobalOverrideDiagnostics(root, index, transitiveRequiredUris, diagnostics);

        // Phase 7: redundant require diagnostics.
        EmitRedundantRequireDiagnostics(requireCalls, index, fileHelper, diagnostics);

        // Phase 8: duplicate require diagnostics.
        EmitDuplicateRequireDiagnostics(requireCalls, diagnostics);

        return diagnostics;
    }

    private static List<RequireCall> CollectRequireCalls(
        SyntaxNode root, IEnumerable<string> workspaceUris, IFileHelper fileHelper)
    {
        var uriList = workspaceUris as ICollection<string> ?? workspaceUris.ToList();
        var calls = new List<RequireCall>();

        foreach (var call in root.DescendantNodes().OfType<FunctionCallExpressionSyntax>())
        {
            if (call.Expression is not IdentifierNameSyntax { Name: "require" }) continue;

            var arg = ExtractStringArg(call);
            if (arg is null) continue;

            if (LuaRequireResolver.IsRelative(arg)) continue;

            var resolved = LuaRequireResolver.Resolve(arg, uriList, fileHelper);
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
        HashSet<string> locallyBound,
        IReadOnlySet<string> requiredUris,
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
            if (locallyBound.Contains(name)) continue;
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

        return map;
    }

    // ── phase 4: unused require ───────────────────────────────────────────────

    private static void EmitUnusedRequireDiagnostics(
        List<RequireCall> requireCalls,
        HashSet<string> usedIdentifiers,
        GameIndex index,
        IFileHelper fileHelper,
        List<LspDiagnostic> diagnostics)
    {
        foreach (var (arg, resolvedUri, callNode) in requireCalls)
        {
            if (resolvedUri is null) continue;

            // Get all globals transitively reachable through the required file.
            var transitiveUris = LuaTransitiveRequireResolver.GetTransitiveDependencies(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { resolvedUri },
                index.Documents,
                fileHelper);

            var hasUsedGlobal = transitiveUris.Any(uri =>
                index.Documents.TryGetValue(uri, out var doc) &&
                doc.Symbols.Any(s => s.Kind == GameSymbolKind.LuaGlobal &&
                                     usedIdentifiers.Contains(s.Id)));

            if (hasUsedGlobal) continue;

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

    // ── phase 5: cyclic require ───────────────────────────────────────────────

    private static void EmitCyclicRequireDiagnostics(
        string documentUri,
        List<RequireCall> requireCalls,
        GameIndex index,
        IFileHelper fileHelper,
        List<LspDiagnostic> diagnostics)
    {
        foreach (var (arg, resolvedUri, callNode) in requireCalls)
        {
            if (resolvedUri is null) continue;

            var transitiveOfRequired = LuaTransitiveRequireResolver.GetTransitiveDependencies(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { resolvedUri },
                index.Documents,
                fileHelper);

            if (!transitiveOfRequired.Contains(documentUri)) continue;

            var span = callNode.GetLocation().GetLineSpan();
            var start = span.StartLinePosition;
            var end = span.EndLinePosition;

            diagnostics.Add(new LspDiagnostic
            {
                Severity = LspDiagnosticSeverity.Error,
                Message = $"require(\"{arg}\") creates a cyclic dependency.",
                Range = new LspRange(
                    new LspPosition(start.Line, start.Character),
                    new LspPosition(end.Line, end.Character)),
                Source = AppProperties.LspServerId
            });
        }
    }

    // ── phase 6: global override ──────────────────────────────────────────────

    private static void EmitGlobalOverrideDiagnostics(
        SyntaxNode root,
        GameIndex index,
        IReadOnlySet<string> transitiveRequiredUris,
        List<LspDiagnostic> diagnostics)
    {
        var requiredGlobalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var uri in transitiveRequiredUris)
            if (index.Documents.TryGetValue(uri, out var doc))
                foreach (var sym in doc.Symbols)
                    if (sym.Kind == GameSymbolKind.LuaGlobal)
                        requiredGlobalNames.Add(sym.Id);

        foreach (var funcDecl in root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>())
        {
            if (funcDecl.Name is not SimpleFunctionNameSyntax simpleName) continue;
            var name = simpleName.Name.Text;
            if (!requiredGlobalNames.Contains(name)) continue;

            var hasOverride = funcDecl.GetFirstToken().LeadingTrivia
                .Any(t => t.ToFullString().TrimStart()
                    .StartsWith("---@Override", StringComparison.OrdinalIgnoreCase));
            if (hasOverride) continue;

            var nameSpan = simpleName.Name.GetLocation().GetLineSpan();
            var start = nameSpan.StartLinePosition;
            var end = nameSpan.EndLinePosition;

            diagnostics.Add(new LspDiagnostic
            {
                Severity = LspDiagnosticSeverity.Warning,
                Message = $"Global '{name}' overrides a symbol from a required file. " +
                          "Add ---@Override to suppress.",
                Range = new LspRange(
                    new LspPosition(start.Line, start.Character),
                    new LspPosition(end.Line, end.Character)),
                Source = AppProperties.LspServerId
            });
        }
    }

    // ── phase 7: redundant require ────────────────────────────────────────────

    private static void EmitRedundantRequireDiagnostics(
        List<RequireCall> requireCalls,
        GameIndex index,
        IFileHelper fileHelper,
        List<LspDiagnostic> diagnostics)
    {
        var resolved = requireCalls.Where(r => r.ResolvedUri is not null).ToList();
        if (resolved.Count < 2) return;

        foreach (var (arg, resolvedUri, callNode) in resolved)
        {
            // resolvedUri is non-null here: `resolved` was filtered by r.ResolvedUri is not null.
            var thisUri = resolvedUri!;

            // Compute the transitive closure of every OTHER direct require.
            var otherUris = resolved
                .Where(r => !string.Equals(r.ResolvedUri, thisUri, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.ResolvedUri!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var transitiveOfOthers = LuaTransitiveRequireResolver.GetTransitiveDependencies(
                otherUris, index.Documents, fileHelper);

            if (!transitiveOfOthers.Contains(thisUri)) continue;

            // Find which direct require already covers this one (for the message).
            var coveringArg = resolved
                .Where(r => !string.Equals(r.ResolvedUri, thisUri, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(r =>
                {
                    var t = LuaTransitiveRequireResolver.GetTransitiveDependencies(
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { r.ResolvedUri! },
                        index.Documents, fileHelper);
                    return t.Contains(thisUri);
                }).Arg;

            var span = callNode.GetLocation().GetLineSpan();
            var start = span.StartLinePosition;
            var end = span.EndLinePosition;

            diagnostics.Add(new LspDiagnostic
            {
                Code = new LspDiagnosticCode(LuaDiagnosticCodes.RedundantRequire),
                Severity = LspDiagnosticSeverity.Warning,
                Message =
                    $"require(\"{arg}\") is redundant, it is already transitively included by require(\"{coveringArg}\").",
                Range = new LspRange(
                    new LspPosition(start.Line, start.Character),
                    new LspPosition(end.Line, end.Character)),
                Source = AppProperties.LspServerId
            });
        }
    }

    // ── phase 8: duplicate require ────────────────────────────────────────────

    private static void EmitDuplicateRequireDiagnostics(
        List<RequireCall> requireCalls,
        List<LspDiagnostic> diagnostics)
    {
        // Key: resolved URI when available (case-insensitive), otherwise the raw arg.
        var seen = new Dictionary<string, RequireCall>(StringComparer.OrdinalIgnoreCase);

        foreach (var rc in requireCalls)
        {
            var key = rc.ResolvedUri ?? rc.Arg;
            if (!seen.TryGetValue(key, out var first))
            {
                seen[key] = rc;
                continue;
            }

            var firstLine = first.Node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var span = rc.Node.GetLocation().GetLineSpan();
            var start = span.StartLinePosition;
            var end = span.EndLinePosition;

            diagnostics.Add(new LspDiagnostic
            {
                Code = new LspDiagnosticCode(LuaDiagnosticCodes.DuplicateRequire),
                Severity = LspDiagnosticSeverity.Warning,
                Message = $"require(\"{rc.Arg}\") is a duplicate; already required on line {firstLine}.",
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

    // ── locally bound names ───────────────────────────────────────────────────

    private static HashSet<string> CollectLocallyBoundNames(
        SyntaxNode root, GameIndex index, string documentUri)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Globals declared in this file (from the workspace index).
        foreach (var (id, syms) in index.WorkspaceDefinitions)
            if (syms.Any(s => s.Kind == GameSymbolKind.LuaGlobal &&
                              s.Origin is FileOrigin fo &&
                              string.Equals(fo.Uri, documentUri, StringComparison.OrdinalIgnoreCase)))
                names.Add(id);

        // 2. Local variable declarations: local x, y = ...
        foreach (var lv in root.DescendantNodes().OfType<LocalVariableDeclarationStatementSyntax>())
        foreach (var decl in lv.Names)
            names.Add(decl.Name);

        // 3. Local function declarations: local function Foo() end
        foreach (var lf in root.DescendantNodes().OfType<LocalFunctionDeclarationStatementSyntax>())
            names.Add(lf.Name.Name);

        // 4. Function parameters in all function declarations (global and local).
        foreach (var funcDecl in root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>())
            CollectParameterNames(funcDecl.Parameters, names);
        foreach (var localFunc in root.DescendantNodes().OfType<LocalFunctionDeclarationStatementSyntax>())
            CollectParameterNames(localFunc.Parameters, names);

        return names;
    }

    private static void CollectParameterNames(ParameterListSyntax? paramList, HashSet<string> names)
    {
        if (paramList is null) return;
        foreach (var p in paramList.Parameters)
            if (p is NamedParameterSyntax np)
                names.Add(np.Identifier.Text);
    }

    // ── phase 1 ───────────────────────────────────────────────────────────────

    private sealed record RequireCall(string Arg, string? ResolvedUri, FunctionCallExpressionSyntax Node);
}