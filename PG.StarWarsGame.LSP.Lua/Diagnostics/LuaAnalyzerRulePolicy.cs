// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using Loretta.CodeAnalysis.Lua.Syntax;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using SyntaxTree = Loretta.CodeAnalysis.SyntaxTree;

namespace PG.StarWarsGame.LSP.Lua.Diagnostics;

/// <summary>
///     Which of the Lua analyzer's findings reach the editor, and under which id.
/// </summary>
/// <remarks>
///     <para>
///         An allow list. Counted over vanilla FoC and EaW with every script in an analyzer of its
///         own (#279, emmylua_ls 0.25.1), the type rules' findings describe the stubs or the
///         analyzer's inference, not the scripts - need-check-nil 919 and 226, param-type-mismatch
///         635 and 164, unnecessary-if 128 and 85, call-non-callable 125 and 13, undefined-field 78
///         and 50, assign-type-mismatch 53 and 44 - and stay dropped. Global scope and require
///         resolution are this server's. What is left holds for any Lua and is mapped, and so is a
///         surplus argument to a Lua function, which Lua drops without a word (vanilla passes
///         SpawnList a callback it never takes), and so is a member an engine type does not have:
///         the engine defines all of its members. Over vanilla EaW undefined-field reports 7 times,
///         twice on an engine type (Is_Evil on a Player, GetFloat on GameRandom) and five times on a
///         Lua table, which is the script's own business.
///     </para>
///     <para>
///         A rule not listed is dropped, including one a later analyzer adds: an id is a
///         suppression contract in author files, so it is given by hand, never on the fly.
///     </para>
/// </remarks>
public static partial class LuaAnalyzerRulePolicy
{
    /// <summary>The analyzer rule names that are kept, and the id each is published under.</summary>
    public static readonly IReadOnlyDictionary<string, DiagnosticId> MappedRules =
        new Dictionary<string, DiagnosticId>(StringComparer.Ordinal)
        {
            ["syntax-error"] = DiagnosticIds.LuaSyntaxError,
            ["unused"] = DiagnosticIds.LuaAnalyzerUnused,
            ["redefined-local"] = DiagnosticIds.LuaAnalyzerRedefinedLocal,
            ["redefined-label"] = DiagnosticIds.LuaAnalyzerRedefinedLabel,
            ["unreachable-code"] = DiagnosticIds.LuaAnalyzerUnreachableCode,
            ["unbalanced-assignments"] = DiagnosticIds.LuaAnalyzerUnbalancedAssignments,
            ["duplicate-index"] = DiagnosticIds.LuaAnalyzerDuplicateIndex,
            ["iter-variable-reassign"] = DiagnosticIds.LuaAnalyzerIterVariableReassign,
            ["doc-syntax-error"] = DiagnosticIds.LuaAnalyzerDocSyntaxError,
            ["undefined-doc-param"] = DiagnosticIds.LuaAnalyzerUndefinedDocParam,
            ["duplicate-doc-field"] = DiagnosticIds.LuaAnalyzerDuplicateDocField,
            ["redundant-parameter"] = DiagnosticIds.LuaAnalyzerRedundantParameter,
            ["undefined-field"] = DiagnosticIds.LuaAnalyzerUndefinedField
        };

    /// <summary>The finding under this server's id, or null when its rule is dropped.</summary>
    public static Diagnostic? Map(Diagnostic finding)
    {
        if (finding.Code?.String is not { } rule || !MappedRules.TryGetValue(rule, out var id)) return null;
        return finding with { Code = id.ToString(), Source = null };
    }

    /// <summary>
    ///     Whether a mapped finding holds in the document. A surplus argument holds only on a call of a
    ///     function defined in Lua, named plainly: an engine function's call forms are this server's
    ///     own check, measured against the bindings, and a member call is an engine object's as often
    ///     as not. The analyzer marks each surplus argument (measured, 0.25.1), so the call is the
    ///     nearest one whose argument list holds the finding's start.
    /// </summary>
    /// <param name="mapped">The finding under this server's id.</param>
    /// <param name="tree">The document's syntax tree.</param>
    /// <param name="engineNames">What the engine binds into every script, the standard library included.</param>
    public static bool Holds(Diagnostic mapped, SyntaxTree tree, IReadOnlySet<string> engineNames)
    {
        if (mapped.Code?.String != DiagnosticIds.LuaAnalyzerRedundantParameter.ToString()) return true;

        var lines = tree.GetText().Lines;
        var line = mapped.Range.Start.Line;
        if (line >= lines.Count) return false;
        var position = lines[line].Start + mapped.Range.Start.Character;
        var call = tree.GetRoot().FindToken(position).Parent?.AncestorsAndSelf()
            .OfType<FunctionCallExpressionSyntax>()
            .FirstOrDefault(c => c.Argument.Span.Contains(position));
        return call?.Expression is IdentifierNameSyntax { Name: var name } && !engineNames.Contains(name);
    }

    /// <summary>Whether a mapped finding is an undefined member, which holds only on an engine type.</summary>
    public static bool IsUndefinedField(Diagnostic mapped)
    {
        return mapped.Code?.String == DiagnosticIds.LuaAnalyzerUndefinedField.ToString();
    }

    /// <summary>
    ///     Where to hover the receiver of a member finding: the last token of the expression before
    ///     the dot or colon. Null when the finding is on no member access.
    /// </summary>
    public static Position? ReceiverPosition(Diagnostic finding, SyntaxTree tree)
    {
        var lines = tree.GetText().Lines;
        var line = finding.Range.Start.Line;
        if (line >= lines.Count) return null;
        var position = lines[line].Start + finding.Range.Start.Character;
        var receiver = tree.GetRoot().FindToken(position).Parent?.AncestorsAndSelf()
            .Select(n => n switch
            {
                MemberAccessExpressionSyntax access => access.Expression,
                MethodCallExpressionSyntax call => call.Expression,
                _ => null
            })
            .FirstOrDefault(e => e is not null);
        if (receiver is null) return null;
        var at = tree.GetLineSpan(receiver.GetLastToken().Span).StartLinePosition;
        return new Position(at.Line, at.Character);
    }

    /// <summary>
    ///     The type the analyzer's hover gives a receiver: the first name after the colon on the code
    ///     block's first line (<c>(global) PlayerObject: Player {</c>, measured on 0.25.1). Null when
    ///     the hover holds no such line.
    /// </summary>
    public static string? ReceiverTypeOf(Hover? hover)
    {
        var text = hover?.Contents switch
        {
            { HasMarkupContent: true, MarkupContent: { } markup } => markup.Value,
            { HasMarkedStrings: true, MarkedStrings: { } strings } => string.Join("\n", strings.Select(s => s.Value)),
            _ => null
        };
        if (text is null) return null;
        var match = HoverTypeRegex().Match(text);
        return match.Success ? match.Groups["type"].Value : null;
    }

    [GeneratedRegex(@"```lua\r?\n[^\n:]*:\s*(?<type>[A-Za-z_]\w*)")]
    private static partial Regex HoverTypeRegex();
}