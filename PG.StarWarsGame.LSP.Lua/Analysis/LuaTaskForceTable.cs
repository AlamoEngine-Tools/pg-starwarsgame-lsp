// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>
///     The task forces a plan script declares. A plan file assigns a global <c>TaskForce</c> table
///     whose entries are tables; the first string of each entry is the force's name. The engine
///     reads that table when it loads the plan and maps every force into the script as a global
///     of exactly that name, which is why the string literal is where the global is defined.
///     Measured against the vanilla corpus (2026-10-05): 250 names in 228 plan files, all of
///     them written this way.
/// </summary>
internal static class LuaTaskForceTable
{
    public const string TableName = "TaskForce";

    /// <summary>Every declared force, in source order, with the token holding its name.</summary>
    public static IReadOnlyList<(string Name, SyntaxToken Token)> Forces(SyntaxNode root)
    {
        var forces = new List<(string, SyntaxToken)>();
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentStatementSyntax>())
        {
            if (assignment.Variables is not [IdentifierNameSyntax { Name: TableName }])
                continue;
            if (assignment.EqualsValues.Values.FirstOrDefault() is not TableConstructorExpressionSyntax table)
                continue;

            foreach (var field in table.Fields)
            {
                if (field is not UnkeyedTableFieldSyntax { Value: TableConstructorExpressionSyntax entry })
                    continue;
                if (entry.Fields.FirstOrDefault() is not UnkeyedTableFieldSyntax
                    {
                        Value: LiteralExpressionSyntax literal
                    } || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                    continue;

                var name = literal.Token.ValueText;
                if (!string.IsNullOrEmpty(name))
                    forces.Add((name, literal.Token));
            }
        }

        return forces;
    }
}