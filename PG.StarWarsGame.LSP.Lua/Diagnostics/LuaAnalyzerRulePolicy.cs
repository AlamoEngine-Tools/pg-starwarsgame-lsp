// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Lua.Diagnostics;

/// <summary>
///     Which of the Lua analyzer's findings reach the editor, and under which id.
/// </summary>
/// <remarks>
///     <para>
///         An allow list. Measured on vanilla FoC (370 scripts, emmylua_ls 0.25.1, the engine stubs
///         as library), the analyzer's findings are dominated by rules that rest on the stubs'
///         types - missing-parameter 2090, param-type-mismatch 1215, undefined-field 395,
///         redundant-parameter 345, need-check-nil and call-non-callable 125 each, unnecessary-if
///         69, assign-type-mismatch 65 - and those describe the stubs, not the scripts. Global scope
///         and require resolution are this server's. What is left holds for any Lua and is mapped.
///     </para>
///     <para>
///         A rule not listed is dropped, including one a later analyzer adds: an id is a
///         suppression contract in author files, so it is given by hand, never on the fly.
///     </para>
/// </remarks>
public static class LuaAnalyzerRulePolicy
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
            ["duplicate-doc-field"] = DiagnosticIds.LuaAnalyzerDuplicateDocField
        };

    /// <summary>The finding under this server's id, or null when its rule is dropped.</summary>
    public static Diagnostic? Map(Diagnostic finding)
    {
        if (finding.Code?.String is not { } rule || !MappedRules.TryGetValue(rule, out var id)) return null;
        return finding with { Code = id.ToString(), Source = null };
    }
}
