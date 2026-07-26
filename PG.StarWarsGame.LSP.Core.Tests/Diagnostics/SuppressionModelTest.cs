// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;

namespace PG.StarWarsGame.LSP.Core.Tests.Diagnostics;

public sealed class SuppressionModelTest
{
    private static readonly DiagnosticId Asset1 = new(DiagnosticGroup.Assets, 1);
    private static readonly DiagnosticId Asset2 = new(DiagnosticGroup.Assets, 2);
    private static readonly DiagnosticId Story1 = new(DiagnosticGroup.Story, 1);

    // ── matcher ──────────────────────────────────────────────────────────────

    [Fact]
    public void IdMatcher_MatchesOnlyThatId()
    {
        var matcher = SuppressionMatcher.ForId(Asset1);

        Assert.True(matcher.Matches(Asset1));
        Assert.False(matcher.Matches(Asset2));
        Assert.False(matcher.Matches(Story1));
    }

    [Fact]
    public void GroupMatcher_MatchesEveryIdInTheGroup()
    {
        var matcher = SuppressionMatcher.ForGroup(DiagnosticGroup.Assets);

        Assert.True(matcher.Matches(Asset1));
        Assert.True(matcher.Matches(Asset2));
        Assert.False(matcher.Matches(Story1));
    }

    [Theory]
    [InlineData("aetswg-004-0001", false)]
    [InlineData("aetswg-004-*", true)]
    [InlineData("AETSWG-004-*", true)]
    public void TryParse_AcceptsBothWireForms(string text, bool wholeGroup)
    {
        Assert.True(SuppressionMatcher.TryParse(text, out var matcher));
        Assert.Equal(wholeGroup, matcher.IsWholeGroup);
        Assert.Equal(4, matcher.Group);
    }

    // A typo must leave the diagnostic visible. Silently matching something else - or everything -
    // would hide a real problem the author never chose to hide.
    [Theory]
    [InlineData("aetswg-4-*")]
    [InlineData("aetswg-004-")]
    [InlineData("aetswg-*-*")]
    [InlineData("aetswg-004-**")]
    [InlineData("*")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsAnythingElse(string? text)
    {
        Assert.False(SuppressionMatcher.TryParse(text, out _));
    }

    [Fact]
    public void Matcher_RoundTripsThroughItsWireForm()
    {
        foreach (var original in new[]
                 {
                     SuppressionMatcher.ForId(Asset1), SuppressionMatcher.ForGroup(DiagnosticGroup.Story)
                 })
        {
            Assert.True(SuppressionMatcher.TryParse(original.ToString(), out var parsed));
            Assert.Equal(original, parsed);
        }
    }

    // ── ranges ───────────────────────────────────────────────────────────────

    [Fact]
    public void Range_CoversOnlyItsOwnLinesAndId()
    {
        var range = new SuppressionRange(
            SuppressionMatcher.ForId(Asset1), SuppressionScope.Object, 10, 20, 9);

        Assert.True(range.Covers(Asset1, 10));
        Assert.True(range.Covers(Asset1, 20));
        Assert.False(range.Covers(Asset1, 9));
        Assert.False(range.Covers(Asset1, 21));
        Assert.False(range.Covers(Asset2, 15));
    }

    // ── document set ─────────────────────────────────────────────────────────

    [Fact]
    public void GlobalMatcher_AppliesAtEveryLine()
    {
        var set = new DocumentSuppressions([], [SuppressionMatcher.ForGroup(DiagnosticGroup.Assets)]);

        Assert.True(set.IsSuppressed(Asset1, 0));
        Assert.True(set.IsSuppressed(Asset2, 9999));
        Assert.False(set.IsSuppressed(Story1, 0));
    }

    [Fact]
    public void RangeAndGlobal_AreBothConsulted()
    {
        var set = new DocumentSuppressions(
            [new SuppressionRange(SuppressionMatcher.ForId(Story1), SuppressionScope.Node, 5, 5, 4)],
            [SuppressionMatcher.ForId(Asset1)]);

        Assert.True(set.IsSuppressed(Story1, 5));
        Assert.False(set.IsSuppressed(Story1, 6));
        Assert.True(set.IsSuppressed(Asset1, 6));
    }

    [Fact]
    public void Empty_SuppressesNothing()
    {
        Assert.True(DocumentSuppressions.Empty.IsEmpty);
        Assert.False(DocumentSuppressions.Empty.IsSuppressed(Asset1, 0));
    }
}
