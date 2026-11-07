// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Generated;

/// <summary>
///     A plan's task forces are globals the engine creates from its TaskForce table, one TaskForce per
///     entry. The analyzer sees no assignment and types them unknown (measured, 0.25.1), so each gets
///     one, typed, above the table that declares it.
/// </summary>
public sealed class TaskForceAnnotations : ILuaAnnotationSource
{
    public IEnumerable<LuaAnnotationInsertion> For(SyntaxTree tree, string uri)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, token) in LuaTaskForceTable.Forces(tree.GetRoot()))
        {
            // A name that is no identifier is a force the script can only reach through _G.
            if (!IsIdentifier(name) || !declared.Add(name)) continue;
            if (token.Parent?.FirstAncestorOrSelf<AssignmentStatementSyntax>() is not { } table) continue;

            var line = table.GetLocation().GetLineSpan().StartLinePosition.Line;
            yield return new LuaAnnotationInsertion(line,
                ["---@type " + LuaTaskForceTable.WrapperTypeName, name + " = nil"]);
        }
    }

    private static bool IsIdentifier(string name)
    {
        var parsed = SyntaxFactory.ParseExpression(name, options: new LuaParseOptions(LuaSyntaxOptions.Lua51));
        return parsed is IdentifierNameSyntax identifier && identifier.Name == name &&
               !parsed.GetDiagnostics().Any();
    }
}