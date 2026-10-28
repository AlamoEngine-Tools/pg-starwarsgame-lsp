// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Diagnostics;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

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
            Range = new Range(1, 2, 1, 5),
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
        Assert.Equal(new Range(1, 2, 1, 5), mapped.Range);
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

    // Census (#279, every script in an analyzer of its own): vanilla passes a callback SpawnList
    // never takes - Lua drops a surplus argument without a word.
    [Fact]
    public void ASurplusArgument_IsKept()
    {
        var mapped = LuaAnalyzerRulePolicy.Map(Finding("redundant-parameter"));

        Assert.Equal(DiagnosticIds.LuaAnalyzerRedundantParameter.ToString(), mapped!.Code!.Value.String);
    }

    private static readonly IReadOnlySet<string> s_engine =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Find_First_Object", "collectgarbage" };

    private static bool Holds(string text, int line, int character)
    {
        var tree = Loretta.CodeAnalysis.Lua.LuaSyntaxTree.ParseText(text);
        var finding = LuaAnalyzerRulePolicy.Map(Finding("redundant-parameter") with
        {
            Range = new Range(line, character, line, character + 1)
        })!;
        return LuaAnalyzerRulePolicy.Holds(finding, tree, s_engine);
    }

    // The analyzer marks each surplus argument (measured, 0.25.1); the call is the one whose
    // argument list holds it. An engine function's call forms are this server's own check.
    [Theory]
    [InlineData("SpawnList(a, b, Extra)", 0, 16, true)] // a Lua function
    [InlineData("SpawnList(a, b, Extra(1))", 0, 16, true)] // the surplus argument is itself a call
    [InlineData("SpawnList(a, Extra(1, 2))", 0, 22, true)] // the surplus is inside the inner call
    [InlineData("Find_First_Object(\"X\", p)", 0, 23, false)] // the engine's
    [InlineData("collectgarbage(256)", 0, 15, false)] // the standard library's
    [InlineData("unit.Attack_Move(a, b)", 0, 20, false)] // a member: an engine object's, as often as not
    public void ASurplusArgument_IsKept_OnlyOnACallOfALuaFunction(string text, int line, int character,
        bool kept)
    {
        Assert.Equal(kept, Holds(text, line, character));
    }

    [Fact]
    public void AFindingOfAnotherRule_AlwaysHolds()
    {
        var tree = Loretta.CodeAnalysis.Lua.LuaSyntaxTree.ParseText("Find_First_Object(\"X\", p)",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(LuaAnalyzerRulePolicy.Holds(LuaAnalyzerRulePolicy.Map(Finding("unused"))!, tree, s_engine));
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