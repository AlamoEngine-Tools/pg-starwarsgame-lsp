// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Persistence;

namespace PG.StarWarsGame.LSP.Core.Tests.Persistence;

public sealed class TypeVersionTest
{
    // ── parsing ──────────────────────────────────────────────────────────────

    [Fact]
    public void TryParse_ScoutForm_SplitsNamespaceFromVersion()
    {
        Assert.True(TypeVersion.TryParse("aetswg-1.2.0", out var version));

        Assert.Equal("aetswg", version.Namespace);
        Assert.Equal("1.2.0", version.Version.ToString());
    }

    [Fact]
    public void TryParse_RoundTripsThroughToString()
    {
        Assert.True(TypeVersion.TryParse("aetswg-2.0.1", out var version));

        Assert.Equal("aetswg-2.0.1", version.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("aetswg")] // no version
    [InlineData("1.2.0")] // no namespace
    [InlineData("aetswg-")] // empty version
    [InlineData("-1.2.0")] // empty namespace
    [InlineData("aetswg-1.2")] // not a full semantic version
    [InlineData("aetswg-banana")]
    public void TryParse_Malformed_Fails(string raw)
    {
        Assert.False(TypeVersion.TryParse(raw, out _));
    }

    // The namespace may carry dots (Scout's do), and the version's own dashes belong to the
    // pre-release part - so the split is at the FIRST dash, not the last.
    [Fact]
    public void TryParse_NamespaceWithDots_AndPrereleaseVersion()
    {
        Assert.True(TypeVersion.TryParse("aetswg.story-1.0.0-beta.1", out var version));

        Assert.Equal("aetswg.story", version.Namespace);
        Assert.Equal("1.0.0-beta.1", version.Version.ToString());
    }

    // Documents declare their version as a constant, not as a string parsed at startup - a typo
    // there would surface as "unreadable version" against every file the user owns.
    [Fact]
    public void Of_BuildsTheSameValueAsParsing()
    {
        Assert.True(TypeVersion.TryParse("aetswg-1.2.3", out var parsed));

        Assert.Equal(parsed, TypeVersion.Of("aetswg", 1, 2, 3));
    }

    // ── ordering ─────────────────────────────────────────────────────────────

    // The reason the value is parsed rather than string-compared: ordinally "1.10.0" sorts BELOW
    // "1.9.0", so a string compare would skip the migration that the tenth revision needs.
    [Fact]
    public void Compare_TenthMinorIsNewerThanNinth()
    {
        Assert.True(TypeVersion.TryParse("aetswg-1.10.0", out var ten));
        Assert.True(TypeVersion.TryParse("aetswg-1.9.0", out var nine));

        Assert.True(ten.CompareTo(nine) > 0);
        Assert.True(nine.CompareTo(ten) < 0);
        Assert.Equal(0, ten.CompareTo(ten));
    }

    // Any increase migrates, whichever component moved - so a patch bump has to order above its
    // predecessor exactly as a major bump does.
    [Fact]
    public void Compare_PatchBumpOrdersAboveItsPredecessor()
    {
        Assert.True(TypeVersion.TryParse("aetswg-1.0.1", out var patched));
        Assert.True(TypeVersion.TryParse("aetswg-1.0.0", out var original));

        Assert.True(patched.CompareTo(original) > 0);
    }

    // ── the unversioned document ─────────────────────────────────────────────

    // A file written before any of this existed carries no version at all. It is version zero of
    // its namespace, which is what gives the first migration something to declare a From for.
    [Fact]
    public void Zero_IsBelowEveryRealVersion()
    {
        var zero = TypeVersion.Zero("aetswg");

        Assert.Equal("aetswg-0.0.0", zero.ToString());
        Assert.True(TypeVersion.TryParse("aetswg-1.0.0", out var one));
        Assert.True(zero.CompareTo(one) < 0);
    }
}
