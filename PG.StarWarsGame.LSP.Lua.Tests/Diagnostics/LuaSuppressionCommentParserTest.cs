// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Lua.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Parsing;

namespace PG.StarWarsGame.LSP.Lua.Tests.Diagnostics;

/// <summary>
///     Lua's half of the suppression grammar: the same directives, written in Lua comments and
///     anchored to Lua constructs.
/// </summary>
public sealed class LuaSuppressionCommentParserTest
{
    private static readonly DiagnosticId Upvalue = DiagnosticIds.LuaEngineUpvalue;
    private static readonly DiagnosticId Module = DiagnosticIds.LuaUnresolvedModule;

    private static IReadOnlyList<SuppressionRange> Parse(string lua)
    {
        return Scan(lua).Ranges;
    }

    private static SuppressionScan Scan(string lua)
    {
        return LuaSuppressionCommentParser.Parse(ParsedLuaDocument.Parse(lua).Tree);
    }

    // ── comment forms ─────────────────────────────────────────────────────────

    [Fact]
    public void ALineComment_CarriesADirective()
    {
        var range = Assert.Single(Parse($"-- aetswg:suppress {Upvalue}\nlocal x = 1\n"));

        Assert.True(range.Matcher.Matches(Upvalue));
    }

    [Fact]
    public void ABlockComment_CarriesADirective()
    {
        var range = Assert.Single(Parse($"--[[ aetswg:suppress {Upvalue} ]]\nlocal x = 1\n"));

        Assert.True(range.Matcher.Matches(Upvalue));
    }

    // The doc-comment form is still a comment; a directive in one has to work, or the rule would
    // depend on how many dashes the author happened to type.
    [Fact]
    public void ADocComment_CarriesADirective()
    {
        Assert.Single(Parse($"--- aetswg:suppress {Upvalue}\nlocal x = 1\n"));
    }

    [Fact]
    public void OrdinaryComments_ProduceNothing()
    {
        Assert.Empty(Parse("-- just explaining the next line\nlocal x = 1\n"));
    }

    // A directive is only a directive in a comment - never in a string, where it is data.
    [Fact]
    public void DirectiveTextInsideAString_IsNotADirective()
    {
        Assert.Empty(Parse($"local s = \"aetswg:suppress {Upvalue}\"\n"));
    }

    // ── scopes ────────────────────────────────────────────────────────────────

    [Fact]
    public void NodeScope_CoversTheFollowingStatement()
    {
        var range = Assert.Single(Parse(
            $"-- aetswg:suppress {Upvalue}\nlocal x = 1\nlocal y = 2\n"));

        Assert.True(range.Covers(Upvalue, 1));
        Assert.False(range.Covers(Upvalue, 2));
    }

    // A multi-line statement is covered whole, not just its first line.
    [Fact]
    public void NodeScope_CoversAllLinesOfAMultiLineStatement()
    {
        var range = Assert.Single(Parse(
            $"-- aetswg:suppress {Upvalue}\nfunction Foo()\n  local x = 1\nend\nlocal y = 2\n"));

        Assert.True(range.Covers(Upvalue, 3));
        Assert.False(range.Covers(Upvalue, 4));
    }

    [Fact]
    public void ObjectScope_CoversTheEnclosingFunction()
    {
        var range = Assert.Single(Parse(
            $"function Foo()\n  -- aetswg:suppress-object {Upvalue}\n  local x = 1\nend\nlocal y = 2\n"));

        Assert.True(range.Covers(Upvalue, 0));
        Assert.True(range.Covers(Upvalue, 3));
        Assert.False(range.Covers(Upvalue, 4));
    }

    [Fact]
    public void FileScope_CoversEveryLine()
    {
        var range = Assert.Single(Parse(
            $"local x = 1\n-- aetswg:suppress-file {Upvalue}\nlocal y = 2\n"));

        Assert.True(range.Covers(Upvalue, 0));
        Assert.True(range.Covers(Upvalue, 99));
    }

    // Silencing more than was asked for is the worse failure, so a directive whose target does not
    // exist collapses onto its own line rather than running to the end of the file.
    [Fact]
    public void ObjectScopeOutsideAnyFunction_CollapsesToItsOwnLine()
    {
        var range = Assert.Single(Parse(
            $"local x = 1\n-- aetswg:suppress-object {Upvalue}\nlocal y = 2\n"));

        Assert.True(range.Covers(Upvalue, 1));
        Assert.False(range.Covers(Upvalue, 2));
    }

    [Fact]
    public void NodeScopeWithNothingAfterIt_CollapsesToItsOwnLine()
    {
        var range = Assert.Single(Parse($"local x = 1\n-- aetswg:suppress {Upvalue}\n"));

        Assert.True(range.Covers(Upvalue, 1));
        Assert.False(range.Covers(Upvalue, 2));
    }

    // ── grammar reuse ─────────────────────────────────────────────────────────

    [Fact]
    public void ADirectiveNamingSeveralIds_ProducesOneRangePerId()
    {
        var ranges = Parse($"-- aetswg:suppress {Upvalue}, {Module}\nlocal x = 1\n");

        Assert.Equal(2, ranges.Count);
        Assert.Contains(ranges, r => r.Matcher.Matches(Upvalue));
        Assert.Contains(ranges, r => r.Matcher.Matches(Module));
    }

    [Fact]
    public void AReason_IsCarriedOntoTheRange()
    {
        var range = Assert.Single(Parse(
            $"-- aetswg:suppress {Upvalue} reason:: engine calls this one\nlocal x = 1\n"));

        Assert.Equal("engine calls this one", range.Reason);
    }

    // A whole-group wildcard has to work here exactly as it does in XML.
    [Fact]
    public void AGroupWildcard_SilencesTheWholeGroup()
    {
        var range = Assert.Single(Parse("-- aetswg:suppress aetswg-012-*\nlocal x = 1\n"));

        Assert.True(range.Matcher.Matches(new DiagnosticId(DiagnosticGroup.Syntax, 1003)));
        Assert.False(range.Matcher.Matches(Upvalue));
    }

    // ── reported problems ─────────────────────────────────────────────────────

    [Fact]
    public void AMistypedId_IsReportedAgainstTheCommentsOwnLine()
    {
        var problem = Assert.Single(Scan("local x = 1\n-- aetswg:suppress nonsense\nlocal y = 2\n").Problems);

        Assert.Equal(1, problem.Line);
        Assert.Equal(DiagnosticIds.SuppressionUnknownRule, problem.Id);
    }

    [Fact]
    public void ADirectiveNamingNothing_IsReported()
    {
        var problem = Assert.Single(Scan("-- aetswg:suppress-file\nlocal x = 1\n").Problems);

        Assert.Equal(DiagnosticIds.SuppressionNoRules, problem.Id);
    }

    [Fact]
    public void OrdinaryComments_ReportNothing()
    {
        Assert.Empty(Scan("-- just a note\nlocal x = 1\n").Problems);
    }
}
