// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Persistence;

namespace PG.StarWarsGame.LSP.Core.Tests.Persistence;

/// <summary>
///     The gate on the user's own project file. Unlike the sidecars, this one is hand-written, sits
///     in their repository, and is shared with teammates who may be on an older extension.
/// </summary>
public sealed class PgprojFormatTest
{
    // Every .pgproj in existence predates the field, so its absence has to be the ordinary case
    // rather than a fault. It means version one.
    [Fact]
    public void Check_Absent_Loads()
    {
        var check = PgprojFormat.Check(null);

        Assert.True(check.CanLoad);
        Assert.Null(check.Message);
    }

    [Fact]
    public void Check_CurrentVersion_Loads()
    {
        Assert.True(PgprojFormat.Check(PgprojFormat.Current.ToString()).CanLoad);
    }

    // The project file identifies itself the way every other document does - Scout's two fields,
    // one convention across the whole system rather than a second one just for this file.
    [Fact]
    public void Current_IsANamespacedTypeVersion()
    {
        Assert.Equal("aetswg", PgprojFormat.Current.Namespace);
        Assert.Equal("aetswg.ModProject", PgprojFormat.TypeName);
    }

    // A file claiming to be some other document is not this one at an odd version.
    [Fact]
    public void Check_ForeignTypeName_IsRefused()
    {
        var check = PgprojFormat.Check(PgprojFormat.Current.ToString(), "aetswg.SomethingElse");

        Assert.False(check.CanLoad);
        Assert.NotNull(check.Message);
    }

    [Fact]
    public void Check_MatchingTypeName_Loads()
    {
        Assert.True(PgprojFormat.Check(PgprojFormat.Current.ToString(), PgprojFormat.TypeName).CanLoad);
    }

    // ── refusing the future ──────────────────────────────────────────────────

    // Upgrading is enforced. The alternative is reading a file whose shape we do not know and then
    // writing our guess back over it, which is how a project file loses whatever the newer version
    // put in it.
    [Fact]
    public void Check_NewerMajor_IsRefusedAndSaysWhatToDo()
    {
        var check = PgprojFormat.Check("aetswg-2.0.0");

        Assert.False(check.CanLoad);
        Assert.NotNull(check.Message);
        Assert.Contains("2.0.0", check.Message, StringComparison.Ordinal);
        Assert.Contains("update", check.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Any increase, not just a major one - the same rule the sidecars migrate by. A minor or patch
    // bump means somebody's newer extension wrote something this build does not know about.
    [Theory]
    [InlineData("aetswg-1.1.0")]
    [InlineData("aetswg-1.0.1")]
    public void Check_AnyHigherVersion_IsRefused(string declared)
    {
        Assert.False(PgprojFormat.Check(declared).CanLoad);
    }

    // ── damage ───────────────────────────────────────────────────────────────

    // A version we cannot read is not a version we can compare, so it cannot be assumed old. The
    // file says something about itself that we do not understand, and guessing is the failure mode
    // this whole field exists to remove.
    [Theory]
    [InlineData("banana")]
    [InlineData("1.0.0")]
    [InlineData("aetswg-1.0")]
    [InlineData("")]
    [InlineData("   ")]
    public void Check_Unreadable_IsRefusedWithAMessageNamingTheValue(string declared)
    {
        var check = PgprojFormat.Check(declared);

        Assert.False(check.CanLoad);
        Assert.NotNull(check.Message);
    }
}
