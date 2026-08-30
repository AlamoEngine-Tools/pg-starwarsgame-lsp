// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Loretta.CodeAnalysis.Text;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;

namespace PG.StarWarsGame.LSP.Lua.Diagnostics;

/// <summary>
///     Finds suppression directives in a Lua file's comments and resolves each to the lines it
///     covers.
///     <para>
///         The grammar is <see cref="SuppressionDirectiveParser" />, shared with XML and dialog
///         text. Lua supplies only what is its own: which trivia are comments, and what the scope
///         keywords point at - the next statement, or the enclosing function.
///     </para>
/// </summary>
public static class LuaSuppressionCommentParser
{
    public static SuppressionScan Parse(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var text = tree.GetText();
        var ranges = new List<SuppressionRange>();
        var problems = new List<SuppressionCommentProblem>();

        foreach (var trivia in root.DescendantTrivia())
        {
            if (!IsComment(trivia)) continue;

            var result = SuppressionDirectiveParser.Parse(CommentBody(trivia.ToString()));
            if (!result.IsDirective) continue;

            var directiveLine = text.Lines.GetLinePosition(trivia.SpanStart).Line;
            problems.AddRange(result.Problems.Select(p => p.At(directiveLine)));

            if (result.Directive is not { } directive) continue;

            var (start, end) = directive.Scope switch
            {
                SuppressionScope.File => SuppressionSpans.File,
                SuppressionScope.Object => SuppressionSpans.ForTarget(
                    SpanOf(EnclosingFunction(root, trivia), text), directiveLine),
                _ => SuppressionSpans.ForTarget(SpanOf(NextStatement(root, trivia), text), directiveLine)
            };

            foreach (var matcher in directive.Matchers)
                ranges.Add(new SuppressionRange(
                    matcher, directive.Scope, start, end, directiveLine, directive.Reason));
        }

        return new SuppressionScan(ranges, problems);
    }

    private static bool IsComment(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
               || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia);
    }

    /// <summary>
    ///     Comment text without its delimiters, which is what the shared grammar expects. The
    ///     leading dashes are stripped wholesale so <c>--</c>, <c>---</c> and any longer run all
    ///     behave the same - how many dashes an author typed is not meant to change the meaning.
    /// </summary>
    private static string CommentBody(string raw)
    {
        var text = raw.AsSpan().Trim();
        if (text.StartsWith("--")) text = text[2..];

        // Block comment: --[[ ... ]], or --[=[ ... ]=] with any number of equals signs.
        if (text.StartsWith("["))
        {
            text = text[1..].TrimStart('=');
            if (text.StartsWith("[")) text = text[1..];

            text = text.TrimEnd();
            if (text.EndsWith("]")) text = text[..^1];
            text = text.TrimEnd('=');
            if (text.EndsWith("]")) text = text[..^1];
        }
        else
        {
            text = text.TrimStart('-');
        }

        return text.ToString();
    }

    private static (int Start, int End)? SpanOf(SyntaxNode? node, SourceText text)
    {
        if (node is null) return null;

        var span = node.Span;
        return (text.Lines.GetLinePosition(span.Start).Line,
            text.Lines.GetLinePosition(span.End).Line);
    }

    /// <summary>
    ///     The statement the directive sits in front of. Trivia attaches to the token that follows
    ///     it, so the statement is found by walking up from that token rather than by scanning
    ///     forward through the tree.
    /// </summary>
    private static SyntaxNode? NextStatement(SyntaxNode root, SyntaxTrivia trivia)
    {
        var token = root.FindToken(trivia.SpanStart, true);
        if (token.SpanStart < trivia.Span.End) return null;

        for (var n = token.Parent; n is not null; n = n.Parent)
            if (n is StatementSyntax)
                return n;

        return null;
    }

    private static SyntaxNode? EnclosingFunction(SyntaxNode root, SyntaxTrivia trivia)
    {
        var token = root.FindToken(trivia.SpanStart, true);

        for (var n = token.Parent; n is not null; n = n.Parent)
            if (n is FunctionDeclarationStatementSyntax
                or LocalFunctionDeclarationStatementSyntax
                or AnonymousFunctionExpressionSyntax)
                return n;

        return null;
    }
}
