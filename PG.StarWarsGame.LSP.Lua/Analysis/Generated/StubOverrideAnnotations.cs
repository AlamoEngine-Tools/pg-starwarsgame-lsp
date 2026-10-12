// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua.Syntax;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Generated;

/// <summary>What one source of knowledge wants inserted above a script's functions.</summary>
public interface ILuaAnnotationSource
{
    IEnumerable<LuaAnnotationInsertion> For(SyntaxTree tree, string uri);
}

/// <summary>
///     A script function defining a global the stubs declare gets the stub's description, parameter
///     types and return type - the analyzer applies a stub's to neither the definition nor its callers
///     (measured, 0.25.1). Parameters take the script's own names, by position.
/// </summary>
public sealed class StubOverrideAnnotations(ILuaApiSchemaProvider schema) : ILuaAnnotationSource
{
    public IEnumerable<LuaAnnotationInsertion> For(SyntaxTree tree, string uri)
    {
        var text = tree.GetText();
        foreach (var function in tree.GetRoot().DescendantNodes().OfType<FunctionDeclarationStatementSyntax>())
        {
            if (function.Name is not SimpleFunctionNameSyntax simple) continue;
            var name = simple.Name.Text;
            if (!schema.AllFunctionNames.Contains(name)) continue;

            var line = function.GetLocation().GetLineSpan().StartLinePosition.Line;
            if (LuaAnnotations.AuthorAnnotated(text, line)) continue;

            var lines = new List<string>();
            if (schema.GetFunctionDescription(name) is { Length: > 0 } description)
                lines.AddRange(description.Split('\n').Select(l => "--- " + l.TrimEnd('\r')));

            var stubParams = schema.GetFunctionParams(name);
            var own = function.Parameters.Parameters.ToList();
            for (var i = 0; i < own.Count && i < stubParams.Count; i++)
                if (own[i] is NamedParameterSyntax named)
                    lines.Add($"---@param {named.Identifier.Text}{(stubParams[i].IsOptional ? "?" : "")} " +
                              stubParams[i].Type.Raw);

            if (schema.GetReturnTypeName(name) is { } returnType) lines.Add("---@return " + returnType);
            if (lines.Count > 0) yield return new LuaAnnotationInsertion(line, lines);
        }
    }
}

internal static class LuaAnnotations
{
    // The author's own annotation wins: a `---` line right above the function means the author
    // documented it, and nothing is generated there.
    public static bool AuthorAnnotated(Loretta.CodeAnalysis.Text.SourceText text, int line)
    {
        return line > 0 && text.Lines[line - 1].ToString().TrimStart().StartsWith("---", StringComparison.Ordinal);
    }
}