// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Diagnostics;

namespace PG.StarWarsGame.LSP.Lua.Tests.Diagnostics;

/// <summary>What of the analyzer's findings reaches the editor, and under which id.</summary>
public sealed class LuaAnalyzerRulePolicyTest
{
    private static Diagnostic Finding(string rule, DiagnosticSeverity severity = DiagnosticSeverity.Warning)
    {
        return new Diagnostic
        {
            Code = rule,
            Severity = severity,
            Message = "message of " + rule,
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(1, 2, 1, 5),
            Tags = new Container<DiagnosticTag>(DiagnosticTag.Unnecessary)
        };
    }

    [Fact]
    public void AMappedRule_GetsItsOwnId_AndKeepsWhatTheAnalyzerSaid()
    {
        var mapped = LuaAnalyzerRulePolicy.Map(Finding("unused", DiagnosticSeverity.Hint));

        Assert.NotNull(mapped);
        Assert.Equal(DiagnosticIds.LuaAnalyzerUnused.ToString(), mapped.Code!.Value.String);
        Assert.Equal(DiagnosticSeverity.Hint, mapped.Severity);
        Assert.Equal("message of unused", mapped.Message);
        Assert.Equal(new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(1, 2, 1, 5), mapped.Range);
        Assert.Contains(DiagnosticTag.Unnecessary, mapped.Tags!.ToList());
    }

    [Fact]
    public void TheAnalyzersSyntaxError_IsASyntaxDiagnostic()
    {
        var mapped = LuaAnalyzerRulePolicy.Map(Finding("syntax-error", DiagnosticSeverity.Error));

        Assert.Equal(DiagnosticIds.LuaSyntaxError.ToString(), mapped!.Code!.Value.String);
        Assert.Equal((int)DiagnosticGroup.Syntax, DiagnosticIds.LuaSyntaxError.Group);
    }

    // Measured on vanilla FoC (370 files): these come from the stubs not yet typing the engine
    // fully, or are this server's to report, or a style preference - never a finding.
    [Theory]
    [InlineData("param-type-mismatch")]
    [InlineData("missing-parameter")]
    [InlineData("redundant-parameter")]
    [InlineData("undefined-field")]
    [InlineData("need-check-nil")]
    [InlineData("call-non-callable")]
    [InlineData("unnecessary-if")]
    [InlineData("assign-type-mismatch")]
    [InlineData("undefined-global")]
    [InlineData("unresolved-require")]
    [InlineData("invert-if")]
    public void StubDependent_OursAndStyle_AreDropped(string rule)
    {
        Assert.Null(LuaAnalyzerRulePolicy.Map(Finding(rule)));
    }

    [Fact]
    public void ARuleThisServerDoesNotKnow_IsDropped()
    {
        // Ids are a suppression contract; a rule a later analyzer adds gets one by hand, or none.
        Assert.Null(LuaAnalyzerRulePolicy.Map(Finding("some-future-rule")));
    }

    [Fact]
    public void EveryMappedRule_HasADistinctIdInTheAnalyzerGroup_ExceptTheSyntaxError()
    {
        var ids = LuaAnalyzerRulePolicy.MappedRules
            .Where(r => r.Key != "syntax-error")
            .Select(r => r.Value)
            .ToList();

        Assert.All(ids, id => Assert.Equal((int)DiagnosticGroup.LuaAnalyzer, id.Group));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
