// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

internal static class LuaImportAnalyzer
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    public static IReadOnlyList<LspDiagnostic> Analyze(
        string documentUri, string text, IReadOnlyDictionary<string, DocumentIndex> documents, IFileHelper fileHelper)
    {
        return Analyze(documentUri, LuaSyntaxTree.ParseText(text, s_parseOptions), documents, fileHelper);
    }

    public static IReadOnlyList<LspDiagnostic> Analyze(
        string documentUri, SyntaxTree tree, IReadOnlyDictionary<string, DocumentIndex> documents,
        IFileHelper fileHelper)
    {
        var root = tree.GetRoot();
        var diagnostics = new List<LspDiagnostic>();

        foreach (var call in root.DescendantNodes().OfType<FunctionCallExpressionSyntax>())
        {
            if (call.Expression is not IdentifierNameSyntax { Name: "require" }) continue;

            var requireArg = ExtractStringArg(call);
            if (requireArg is null) continue;

            var resolved = LuaRequireResolver.Resolve(requireArg, documents, fileHelper, documentUri);
            // Unresolved relative requires are silently skipped - target may be outside the workspace.
            // Unresolved absolute requires are a real error.
            if (resolved is null && !LuaRequireResolver.IsRelative(requireArg))
                diagnostics.Add(BuildDiagnostic(call, requireArg));
        }

        return diagnostics;
    }

    private static string? ExtractStringArg(FunctionCallExpressionSyntax call)
    {
        if (call.Argument is StringFunctionArgumentSyntax strArg)
            return strArg.Expression.Token.ValueText;

        if (call.Argument is ExpressionListFunctionArgumentSyntax exprList &&
            exprList.Expressions.FirstOrDefault() is LiteralExpressionSyntax lit)
            return lit.Token.ValueText;

        return null;
    }

    private static LspDiagnostic BuildDiagnostic(FunctionCallExpressionSyntax call, string requireArg)
    {
        var span = call.GetLocation().GetLineSpan();
        var start = span.StartLinePosition;
        var end = span.EndLinePosition;
        return new LspDiagnostic
        {
            Severity = LspDiagnosticSeverity.Error,
            Message = $"Cannot find module '{requireArg}'. No matching '.lua' file found in workspace.",
            Range = new LspRange(
                new LspPosition(start.Line, start.Character),
                new LspPosition(end.Line, end.Character)),
            Source = AppProperties.LspServerId
        };
    }
}