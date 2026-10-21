// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Schema;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticCode = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticCode;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>
///     Calls to an engine function whose stub lists its call forms (<c>---@overload</c>, measured from
///     the binding's branch order: <c>Find_Nearest</c>, <c>FindDeadlyEnemy</c>) that fit none of them.
///     The analyzer the editor runs checks a call against the signature only, never against the
///     forms, so a call that mixes two forms passes there and does something else in the game.
///     <para>
///         Judged only from what the text shows: the argument count, literal kinds (string, number,
///         boolean, nil, table) and the stub type of an engine global the file leaves alone
///         (<c>PlayerObject</c> is a Player). A value of unknown type fits any position, and a trailing
///         call or <c>...</c> can supply any count, so neither is judged.
///     </para>
/// </summary>
internal static class LuaCallFormAnalyzer
{
    private const string Unknown = "any";

    public static IReadOnlyList<LspDiagnostic> Analyze(string documentUri, SyntaxTree tree,
        ILuaApiSchemaProvider stubs, IMessageCatalog? messages = null)
    {
        messages ??= ResxMessageCatalog.Server("en");
        var root = tree.GetRoot();
        var diagnostics = new List<LspDiagnostic>();
        HashSet<string>? bound = null;

        foreach (var call in root.DescendantNodes().OfType<FunctionCallExpressionSyntax>())
        {
            if (call.Expression is not IdentifierNameSyntax callee) continue;
            var forms = stubs.GetCallForms(callee.Name);
            if (forms.Count == 0) continue;

            var arguments = Arguments(call.Argument);
            if (arguments is null) continue;

            bound ??= NamesTheFileBinds(root);
            var seen = arguments.Select(a => KindOf(a, stubs, bound)).ToList();
            if (forms.Any(form => Fits(form, seen))) continue;

            diagnostics.Add(new LspDiagnostic
            {
                Code = new LspDiagnosticCode(DiagnosticIds.LuaNoCallFormFits.ToString()),
                Severity = LspDiagnosticSeverity.Warning,
                Message = messages.Format("LuaCallForm_NoFormFits", callee.Name, $"({string.Join(", ", seen)})"),
                Range = RangeOf(call.GetLocation()),
                Source = AppProperties.LspServerId
            });
        }

        return diagnostics;
    }

    // The argument expressions, or null where their count is not what the text shows: a trailing call
    // or `...` spreads into as many values as it returns.
    private static IReadOnlyList<ExpressionSyntax>? Arguments(FunctionArgumentSyntax argument)
    {
        switch (argument)
        {
            case ExpressionListFunctionArgumentSyntax list:
                var expressions = list.Expressions.ToList();
                if (expressions.Count > 0 &&
                    expressions[^1] is FunctionCallExpressionSyntax or MethodCallExpressionSyntax
                        or VarArgExpressionSyntax)
                    return null;
                return expressions;
            case StringFunctionArgumentSyntax str:
                return [str.Expression];
            case TableConstructorFunctionArgumentSyntax table:
                return [table.TableConstructor];
            default:
                return null;
        }
    }

    private static string KindOf(ExpressionSyntax expression, ILuaApiSchemaProvider stubs, HashSet<string> bound)
    {
        return expression.Kind() switch
        {
            SyntaxKind.StringLiteralExpression => "string",
            SyntaxKind.NumericalLiteralExpression => "number",
            SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression => "boolean",
            SyntaxKind.NilLiteralExpression => "nil",
            SyntaxKind.TableConstructorExpression => "table",
            _ when expression is IdentifierNameSyntax name && !bound.Contains(name.Name) =>
                stubs.GetGlobalTypeName(name.Name) ?? Unknown,
            _ => Unknown
        };
    }

    // Same count - a form's optional positions may be left out - and every kind the text shows is one
    // the form's position takes.
    private static bool Fits(IReadOnlyList<LuaParamAnnotation> form, IReadOnlyList<string> seen)
    {
        var required = form.Count(p => !p.IsOptional);
        if (seen.Count < required || seen.Count > form.Count) return false;
        for (var i = 0; i < seen.Count; i++)
        {
            if (seen[i] == Unknown) continue;
            var accepted = form[i].Type.Raw.Split('|', StringSplitOptions.TrimEntries);
            if (!accepted.Contains(seen[i], StringComparer.Ordinal) &&
                !accepted.Contains("any", StringComparer.Ordinal))
                return false;
        }

        return true;
    }

    // Every name the file binds itself - locals, parameters, globals it assigns - holds whatever the
    // file put there, so the stub's type for the engine global of that name says nothing about it.
    private static HashSet<string> NamesTheFileBinds(SyntaxNode root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodes())
            switch (node)
            {
                case LocalVariableDeclarationStatementSyntax local:
                    foreach (var name in local.Names) names.Add(name.Name);
                    break;
                case NamedParameterSyntax parameter:
                    names.Add(parameter.Identifier.Text);
                    break;
                case AssignmentStatementSyntax assignment:
                    foreach (var target in assignment.Variables.OfType<IdentifierNameSyntax>()) names.Add(target.Name);
                    break;
            }

        return names;
    }

    private static LspRange RangeOf(Location location)
    {
        var span = location.GetLineSpan();
        return new LspRange(new LspPosition(span.StartLinePosition.Line, span.StartLinePosition.Character),
            new LspPosition(span.EndLinePosition.Line, span.EndLinePosition.Character));
    }
}