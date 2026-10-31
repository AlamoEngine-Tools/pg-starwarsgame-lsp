// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace PG.StarWarsGame.LSP.Lua.Completion;

/// <summary>The variables in reach of a position that only ever hold strings of one kind.</summary>
internal static class LuaStringVariables
{
    /// <summary>
    ///     Every variable visible at <paramref name="position" /> - a global, or a local declared
    ///     before it in an enclosing scope - whose every assignment in the file is a string literal
    ///     <paramref name="accepts" /> takes. A parameter, a loop variable or any other value is
    ///     unknown here, and the variable is not offered.
    /// </summary>
    public static IEnumerable<string> Holding(SyntaxTree tree, int position, Func<string, bool> accepts)
    {
        var script = new Script([tree]);
        var scope = tree.GetRoot().FindToken(Math.Max(0, position - 1)).Parent?.AncestorsAndSelf()
            .Select(script.GetScope).FirstOrDefault(s => s is not null);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (; scope is not null; scope = scope.ContainingScope)
            foreach (var variable in scope.DeclaredVariables)
            {
                if (variable.Kind is not (VariableKind.Local or VariableKind.Global)) continue;
                // A local's statement ends before the position, or the local is not yet declared there.
                if (variable.Kind == VariableKind.Local && variable.Declaration?.Span.End > position) continue;
                // The nearest declaration of a name hides the others.
                if (!seen.Add(variable.Name)) continue;

                // Loretta records the statement that writes, not the name written.
                var writes = variable.WriteLocations.ToList();
                if (writes.Count > 0 && writes.All(w => StringWritten(w, variable.Name) is { } value && accepts(value)))
                    yield return variable.Name;
            }
    }

    // The string literal a statement assigns to name, or null for anything else.
    private static string? StringWritten(SyntaxNode statement, string name)
    {
        var value = statement switch
        {
            AssignmentStatementSyntax assignment => ValueAt(
                assignment.Variables.Select(v => (v as IdentifierNameSyntax)?.Name), name,
                assignment.EqualsValues.Values),
            LocalVariableDeclarationStatementSyntax local => ValueAt(local.Names.Select(n => n.IdentifierName.Name),
                name, local.EqualsValues?.Values),
            _ => null
        };
        return value is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
    }

    // The value assigned to the last target of that name; a target without a value gets nil.
    private static ExpressionSyntax? ValueAt(IEnumerable<string?> targets, string name,
        SeparatedSyntaxList<ExpressionSyntax>? values)
    {
        var index = targets.Select((target, i) => (target, i)).LastOrDefault(t => t.target == name, (null, -1)).i;
        return index >= 0 && values is { } list && index < list.Count ? list[index] : null;
    }
}