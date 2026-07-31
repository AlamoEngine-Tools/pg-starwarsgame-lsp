// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;

namespace PG.StarWarsGame.LSP.Core.Tests.Diagnostics.Suppression;

/// <summary>
///     The directive grammar, independent of any document model - the comment body arrives with its
///     language's delimiters already stripped, so the same rules serve XML, Lua and dialog text.
/// </summary>
public sealed class SuppressionDirectiveParserTest
{
    private static SuppressionDirective Parse(string body)
    {
        Assert.True(SuppressionDirectiveParser.TryParse(body, out var directive), $"failed to parse: {body}");
        return directive;
    }

    // ── scopes ────────────────────────────────────────────────────────────────

    [Fact]
    public void BareKeyword_IsNodeScope()
    {
        Assert.Equal(SuppressionScope.Node, Parse(" aetswg:suppress aetswg-004-0001 ").Scope);
    }

    [Theory]
    [InlineData("-object", SuppressionScope.Object)]
    [InlineData("-file", SuppressionScope.File)]
    public void ScopeSuffix_SelectsTheScope(string suffix, SuppressionScope expected)
    {
        Assert.Equal(expected, Parse($"aetswg:suppress{suffix} aetswg-004-0001").Scope);
    }

    [Fact]
    public void KeywordAndIds_AreCaseInsensitive()
    {
        var directive = Parse("AETSWG:SUPPRESS-FILE AETSWG-004-0001");

        Assert.Equal(SuppressionScope.File, directive.Scope);
        Assert.True(Assert.Single(directive.Matchers).Matches(new DiagnosticId(4, 1)));
    }

    // ── rule lists ────────────────────────────────────────────────────────────

    [Fact]
    public void SingleId_YieldsOneMatcher()
    {
        Assert.Single(Parse("aetswg:suppress aetswg-004-0001").Matchers);
    }

    [Fact]
    public void CommaSeparatedIds_YieldOneMatcherEach()
    {
        var matchers = Parse("aetswg:suppress aetswg-004-0001, aetswg-010-0002,aetswg-006-0003").Matchers;

        Assert.Equal(3, matchers.Count);
        Assert.All(matchers, m => Assert.False(m.IsWholeGroup));
    }

    [Fact]
    public void AGroupWildcard_MayAppearInAList()
    {
        var matchers = Parse("aetswg:suppress aetswg-004-0001, aetswg-010-*").Matchers;

        Assert.Equal(2, matchers.Count);
        Assert.Contains(matchers, m => m.IsWholeGroup && m.Group == (int)DiagnosticGroup.Symbols);
    }

    // A typo in one entry must not cost the user the entries they got right - and must not be
    // guessed at either. The bad entry silences nothing, so its diagnostic stays visible.
    [Fact]
    public void MalformedEntry_IsDroppedAndTheValidOnesSurvive()
    {
        var matchers = Parse("aetswg:suppress aetswg-004-0001, aetswg-04-2, aetswg-010-0002").Matchers;

        Assert.Equal(2, matchers.Count);
    }

    [Fact]
    public void EveryEntryMalformed_FailsToParse()
    {
        Assert.False(SuppressionDirectiveParser.TryParse("aetswg:suppress aetswg-04-2, nonsense", out _));
    }

    [Fact]
    public void KeywordWithNoIds_FailsToParse()
    {
        Assert.False(SuppressionDirectiveParser.TryParse("aetswg:suppress", out _));
    }

    // ── reason ────────────────────────────────────────────────────────────────

    [Fact]
    public void ReasonMarker_CapturesTheRemainingText()
    {
        var directive = Parse("aetswg:suppress aetswg-004-0001 reason:: icon is generated at build time");

        Assert.Equal("icon is generated at build time", directive.Reason);
    }

    [Fact]
    public void NoReasonMarker_LeavesTheReasonUnset()
    {
        Assert.Null(Parse("aetswg:suppress aetswg-004-0001").Reason);
    }

    // Free text after the ids without the marker is not a reason - it is ignored, exactly as it was
    // before the marker existed, so nothing already written changes meaning.
    [Fact]
    public void TextWithoutTheMarker_IsIgnoredRatherThanTreatedAsAReason()
    {
        var directive = Parse("aetswg:suppress aetswg-004-0001 icon is generated");

        Assert.Null(directive.Reason);
        Assert.Single(directive.Matchers);
    }

    // The reason is free text; commas in it must not be mistaken for more rule ids.
    [Fact]
    public void CommasInsideTheReason_AreNotParsedAsIds()
    {
        var directive = Parse(
            "aetswg:suppress aetswg-004-0001 reason:: generated, along with its icon, at build time");

        Assert.Single(directive.Matchers);
        Assert.Equal("generated, along with its icon, at build time", directive.Reason);
    }

    [Fact]
    public void ReasonMarker_IsCaseInsensitiveAndTrimmed()
    {
        Assert.Equal("deliberate", Parse("aetswg:suppress aetswg-004-0001 REASON::   deliberate  ").Reason);
    }

    [Fact]
    public void EmptyReason_IsTreatedAsAbsent()
    {
        Assert.Null(Parse("aetswg:suppress aetswg-004-0001 reason::   ").Reason);
    }

    // ── non-directives ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("just an ordinary comment")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("aetswg-004-0001")] // an id with no keyword is not a directive
    [InlineData("suppress aetswg-004-0001")] // keyword without the namespace
    public void NonDirectiveText_FailsToParse(string body)
    {
        Assert.False(SuppressionDirectiveParser.TryParse(body, out _));
    }

    // The directive may sit alongside other prose in the same comment.
    [Fact]
    public void DirectiveEmbeddedInSurroundingProse_IsStillFound()
    {
        var directive = Parse("TODO revisit - aetswg:suppress aetswg-004-0001 reason:: pending art");

        Assert.Single(directive.Matchers);
        Assert.Equal("pending art", directive.Reason);
    }

    // ── reported problems ─────────────────────────────────────────────────────
    //
    // A directive that silences nothing used to fail silently: the user saw the diagnostic still
    // sitting there and had nothing to tell them their comment was the problem. These are what
    // turns that into something reportable.

    [Fact]
    public void OrdinaryComment_ReportsNothing()
    {
        var result = SuppressionDirectiveParser.Parse("just an ordinary comment");

        Assert.False(result.IsDirective);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void WellFormedDirective_ReportsNothing()
    {
        var result = SuppressionDirectiveParser.Parse("aetswg:suppress aetswg-004-0001, aetswg-010-*");

        Assert.True(result.IsDirective);
        Assert.NotNull(result.Directive);
        Assert.Empty(result.Problems);
    }

    [Theory]
    [InlineData("aetswg:suppress")]
    [InlineData("aetswg:suppress-file")]
    [InlineData("aetswg:suppress reason:: I forgot the id")]
    public void DirectiveNamingNothing_IsReported(string body)
    {
        var problem = Assert.Single(SuppressionDirectiveParser.Parse(body).Problems);

        Assert.Equal(SuppressionProblemKind.NoRules, problem.Kind);
        Assert.Equal(DiagnosticIds.SuppressionNoRules, problem.Id);
    }

    [Fact]
    public void MalformedId_IsReportedAndNamedInTheMessage()
    {
        var result = SuppressionDirectiveParser.Parse("aetswg:suppress aetswg-4-1");

        Assert.Null(result.Directive);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(SuppressionProblemKind.UnknownRule, problem.Kind);
        Assert.Equal(DiagnosticIds.SuppressionUnknownRule, problem.Id);
        Assert.Contains("aetswg-4-1", problem.Message, StringComparison.Ordinal);
    }

    // The whole point of parsing entries independently: the good ones still work, and the user is
    // told about exactly the one that does not.
    [Fact]
    public void MixedList_KeepsTheValidIdsAndReportsOnlyTheBadOne()
    {
        var result = SuppressionDirectiveParser.Parse(
            "aetswg:suppress aetswg-004-0001, nonsense, aetswg-010-0002");

        Assert.Equal(2, result.Directive!.Matchers.Count);
        Assert.Contains("nonsense", Assert.Single(result.Problems).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralMalformedIds_AreEachReported()
    {
        var result = SuppressionDirectiveParser.Parse("aetswg:suppress nope, alsonope");

        Assert.Equal(2, result.Problems.Count);
    }

    // A trailing comma is a typing slip, not a claim about a diagnostic - there is nothing to name
    // in the message, so reporting it would only be noise.
    [Fact]
    public void TrailingComma_IsNotReported()
    {
        Assert.Empty(SuppressionDirectiveParser.Parse("aetswg:suppress aetswg-004-0001,").Problems);
    }

    // A malformed directive must not also count as "names nothing" - one comment, one complaint.
    [Fact]
    public void MalformedId_DoesNotAlsoReportAnEmptyList()
    {
        var kinds = SuppressionDirectiveParser.Parse("aetswg:suppress nope")
            .Problems.Select(p => p.Kind);

        Assert.Equal([SuppressionProblemKind.UnknownRule], kinds);
    }

    [Fact]
    public void ProblemsCarryTheLineTheyAreAnchoredTo()
    {
        var problem = Assert.Single(SuppressionDirectiveParser.Parse("aetswg:suppress nope").Problems).At(7);

        Assert.Equal(7, problem.Line);
        Assert.Equal(DiagnosticIds.SuppressionUnknownRule, problem.Id);
    }
}
