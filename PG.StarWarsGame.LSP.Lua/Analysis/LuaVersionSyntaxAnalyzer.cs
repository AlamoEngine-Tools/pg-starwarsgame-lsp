// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticCode = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticCode;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>
///     Syntax Lua 5.1 added, which the game's Lua 5.0.2 does not parse (#157). Measured on the game's
///     own lexer and parser: the unary operators are '-' and 'not', the binary ones have no '%', a
///     simple expression is never '...' and a primary one only '(' or a name ("unexpected symbol"
///     otherwise); a long string opens only on "[[", a long comment only on "--[[". A script that
///     fails to parse fails to load, and the game attaches no script. This server's parser and the
///     analyzer both read 5.1, so neither reports any of it. Counted: none in the EaW or FoC
///     scripts; two in about 10,000 files of 13 mods, both in one test file.
/// </summary>
internal static class LuaVersionSyntaxAnalyzer
{
    private const string NotLoaded = "The game runs Lua 5.0 and does not load this script";

    public static IReadOnlyList<LspDiagnostic> Analyze(SyntaxTree tree)
    {
        var diagnostics = new List<LspDiagnostic>();
        var root = tree.GetRoot();
        foreach (var node in root.DescendantNodes())
            switch (node)
            {
                case UnaryExpressionSyntax length when length.IsKind(SyntaxKind.LengthExpression):
                    diagnostics.Add(Diagnostic(length.OperatorToken.GetLocation(),
                        $"The length operator '#' is Lua 5.1: {NotLoaded}; use table.getn for a table, string.len for a string."));
                    break;
                case BinaryExpressionSyntax modulo when modulo.IsKind(SyntaxKind.ModuloExpression):
                    diagnostics.Add(Diagnostic(modulo.OperatorToken.GetLocation(),
                        $"The modulo operator '%' is Lua 5.1: {NotLoaded}."));
                    break;
                case VarArgExpressionSyntax varargs:
                    diagnostics.Add(Diagnostic(varargs.GetLocation(),
                        $"'...' as a value is Lua 5.1: {NotLoaded}; a Lua 5.0 vararg function reads its extra arguments from the table 'arg'."));
                    break;
            }

        foreach (var token in root.DescendantTokens())
            if (token.IsKind(SyntaxKind.StringLiteralToken) && token.Text.StartsWith("[=", StringComparison.Ordinal))
                diagnostics.Add(Diagnostic(token.GetLocation(), LevelMessage));

        // The game's lexer opens a long comment only on "--[[": "--[=[" is a line comment there. One
        // line long, nothing changes; longer, every line after the first is read as code.
        foreach (var trivia in root.DescendantTrivia())
            if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) &&
                trivia.ToString().StartsWith("--[=", StringComparison.Ordinal) &&
                trivia.GetLocation().GetLineSpan() is var span &&
                span.EndLinePosition.Line > span.StartLinePosition.Line)
                diagnostics.Add(Diagnostic(trivia.GetLocation(),
                    "A long comment with '=' levels is Lua 5.1: Lua 5.0 reads '--[=[' as a line comment, so the game takes the lines after the first as code; Lua 5.0 nests --[[ ]] instead."));

        return diagnostics;
    }

    private static string LevelMessage =>
        $"A long string with '=' levels is Lua 5.1: {NotLoaded}; Lua 5.0 nests [[ ]] instead.";

    private static LspDiagnostic Diagnostic(Location location, string message)
    {
        var span = location.GetLineSpan();
        return new LspDiagnostic
        {
            Code = new LspDiagnosticCode(DiagnosticIds.LuaSyntaxNotInLua50.ToString()),
            Severity = LspDiagnosticSeverity.Error,
            Message = message,
            Range = new LspRange(
                new LspPosition(span.StartLinePosition.Line, span.StartLinePosition.Character),
                new LspPosition(span.EndLinePosition.Line, span.EndLinePosition.Character)),
            Source = AppProperties.LspServerId
        };
    }
}