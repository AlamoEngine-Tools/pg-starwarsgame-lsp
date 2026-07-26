// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Core.Tests.Diagnostics;

public sealed class DiagnosticIdTest
{
    // ── format ───────────────────────────────────────────────────────────────

    // The wire format is fixed by issue #66: aetswg-<3-digit group>-<4-digit number>, always 15
    // characters. It appears in suppression comments the user types by hand, so the padding is
    // part of the contract - "aetswg-1-1" must never be produced.
    [Theory]
    [InlineData(0, 0, "aetswg-000-0000")]
    [InlineData(1, 1, "aetswg-001-0001")]
    [InlineData(12, 345, "aetswg-012-0345")]
    [InlineData(999, 9999, "aetswg-999-9999")]
    public void ToString_PadsToTheFixedWidth(int group, int number, string expected)
    {
        Assert.Equal(expected, new DiagnosticId(group, number).ToString());
        Assert.Equal(15, expected.Length);
    }

    [Fact]
    public void Group_And_Number_AreExposed()
    {
        var id = new DiagnosticId(12, 345);

        Assert.Equal(12, id.Group);
        Assert.Equal(345, id.Number);
    }

    // ── construction guards ──────────────────────────────────────────────────

    // A value that cannot be rendered in the fixed width would produce an unparseable id, so it is
    // rejected at construction rather than silently truncated on the way out.
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1000, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 10000)]
    public void OutOfRangeComponents_AreRejected(int group, int number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticId(group, number));
    }

    // ── parsing ──────────────────────────────────────────────────────────────

    [Fact]
    public void TryParse_RoundTrips()
    {
        Assert.True(DiagnosticId.TryParse("aetswg-012-0345", out var id));
        Assert.Equal(new DiagnosticId(12, 345), id);
    }

    // Users type these into suppression comments, so casing must not matter.
    [Fact]
    public void TryParse_IsCaseInsensitive()
    {
        Assert.True(DiagnosticId.TryParse("AETSWG-012-0345", out var id));
        Assert.Equal(new DiagnosticId(12, 345), id);
    }

    [Fact]
    public void TryParse_ToleratesSurroundingWhitespace()
    {
        Assert.True(DiagnosticId.TryParse("  aetswg-012-0345  ", out var id));
        Assert.Equal(new DiagnosticId(12, 345), id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("aetswg-12-345")] // unpadded
    [InlineData("aetswg-0012-0345")] // over-padded
    [InlineData("aetswg-012-0345-1")] // trailing segment
    [InlineData("other-012-0345")] // wrong prefix
    [InlineData("aetswg-abc-0345")] // non-numeric
    [InlineData("aetswg_012_0345")] // wrong separator
    [InlineData("story-chain")] // a legacy code, not an id
    public void TryParse_RejectsMalformed(string? text)
    {
        Assert.False(DiagnosticId.TryParse(text, out _));
    }

    // ── equality ─────────────────────────────────────────────────────────────

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(new DiagnosticId(1, 2), new DiagnosticId(1, 2));
        Assert.NotEqual(new DiagnosticId(1, 2), new DiagnosticId(2, 1));
    }
}
