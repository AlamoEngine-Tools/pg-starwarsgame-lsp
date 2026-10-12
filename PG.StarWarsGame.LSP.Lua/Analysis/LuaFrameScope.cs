// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua.Syntax;
using Loretta.CodeAnalysis.Text;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>A local variable or parameter that is in scope at a position.</summary>
public sealed record LuaFrameLocal(string Name, bool IsParameter);

/// <summary>
///     The locals and parameters visible at a position. Globals are deliberately not included: a
///     debugger reads those by name from the running game, and only locals need the name list a
///     static parse can supply.
/// </summary>
public static class LuaFrameScope
{
    /// <param name="tree">The parsed document.</param>
    /// <param name="text">The text the tree was parsed from.</param>
    /// <param name="line">Zero-based line.</param>
    /// <param name="character">Zero-based column; locals whose declaration ends after this position are not in scope yet.</param>
    public static IReadOnlyList<LuaFrameLocal> LocalsAt(SyntaxTree tree, string text, int line, int character)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(text);

        var root = tree.GetRoot();
        var offset = ComputeOffset(text, line, character);
        var locals = new List<LuaFrameLocal>();
        CollectLocals(root, offset, locals);
        CollectParameters(root, offset, locals);
        return locals;
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

    private static void CollectLocals(SyntaxNode root, int offset, List<LuaFrameLocal> locals)
    {
        foreach (var local in root.DescendantNodes().OfType<LocalVariableDeclarationStatementSyntax>())
        {
            // Declared before the position, and in a function that contains it (if in one at all).
            if (local.Span.End > offset) continue;
            var enclosing = EnclosingFunctionSpan(local);
            if (enclosing.HasValue && !enclosing.Value.Contains(offset)) continue;

            foreach (var name in local.Names)
                locals.Add(new LuaFrameLocal(name.Name, false));
        }
    }

    // A parameter is in scope anywhere inside its function.
    private static void CollectParameters(SyntaxNode root, int offset, List<LuaFrameLocal> locals)
    {
        foreach (var node in root.DescendantNodes())
        {
            var parameters = node switch
            {
                FunctionDeclarationStatementSyntax f => f.Parameters,
                LocalFunctionDeclarationStatementSyntax f => f.Parameters,
                AnonymousFunctionExpressionSyntax f => f.Parameters,
                _ => null
            };
            if (parameters is null || !node.FullSpan.Contains(offset)) continue;

            foreach (var parameter in parameters.Parameters.OfType<NamedParameterSyntax>())
                locals.Add(new LuaFrameLocal(parameter.Identifier.Text, true));
        }
    }

    private static TextSpan? EnclosingFunctionSpan(SyntaxNode node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            if (parent is FunctionDeclarationStatementSyntax or LocalFunctionDeclarationStatementSyntax
                or AnonymousFunctionExpressionSyntax)
                return parent.FullSpan;
        return null;
    }
}