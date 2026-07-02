// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

internal static class LuaRequireCallLocator
{
    /// <summary>
    ///     Finds the <c>require(...)</c> call whose string-argument span contains
    ///     <paramref name="line" />/<paramref name="character" />, if any.
    /// </summary>
    public static (string ArgText, int StartLine, int StartChar, int EndLine, int EndChar)? TryFindAt(
        SyntaxNode root, int line, int character)
    {
        foreach (var call in root.DescendantNodes().OfType<FunctionCallExpressionSyntax>())
        {
            if (call.Expression is not IdentifierNameSyntax { Name: "require" }) continue;

            string? requireArg = null;
            if (call.Argument is StringFunctionArgumentSyntax strArg)
                requireArg = strArg.Expression.Token.ValueText;
            else if (call.Argument is ExpressionListFunctionArgumentSyntax exprList &&
                     exprList.Expressions.FirstOrDefault() is LiteralExpressionSyntax lit)
                requireArg = lit.Token.ValueText;

            if (requireArg is null) continue;

            var span = GetArgSpan(call);
            if (span is null) continue;

            var (startLine, startChar, endLine, endChar) = span.Value;
            if (line < startLine || line > endLine) continue;
            if (line == startLine && character < startChar) continue;
            if (line == endLine && character > endChar) continue;

            return (requireArg, startLine, startChar, endLine, endChar);
        }

        return null;
    }

    private static (int StartLine, int StartChar, int EndLine, int EndChar)? GetArgSpan(
        FunctionCallExpressionSyntax call)
    {
        if (call.Argument is StringFunctionArgumentSyntax strArg)
        {
            var s = strArg.Expression.Token.GetLocation().GetLineSpan();
            return (s.StartLinePosition.Line, s.StartLinePosition.Character,
                s.EndLinePosition.Line, s.EndLinePosition.Character);
        }

        if (call.Argument is ExpressionListFunctionArgumentSyntax exprList &&
            exprList.Expressions.FirstOrDefault() is LiteralExpressionSyntax lit)
        {
            var s = lit.Token.GetLocation().GetLineSpan();
            return (s.StartLinePosition.Line, s.StartLinePosition.Character,
                s.EndLinePosition.Line, s.EndLinePosition.Character);
        }

        return null;
    }
}
