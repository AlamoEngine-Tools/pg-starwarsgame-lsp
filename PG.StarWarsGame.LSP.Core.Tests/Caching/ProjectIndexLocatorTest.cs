// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Caching;

namespace PG.StarWarsGame.LSP.Core.Tests.Caching;

public sealed class ProjectIndexLocatorTest
{
    [Theory]
    [InlineData("/projects/mymod/mymod.pgproj", "/projects/mymod/.aetswg")]
    [InlineData("/projects/mymod/mymod.pgproj", "/projects/mymod/.aetswg")]
    public void GetAetswgDirectory_ReturnsDirectoryAlongsidePgproj(string pgprojPath, string expected)
    {
        Assert.Equal(expected, ProjectIndexLocator.GetAetswgDirectory(pgprojPath));
    }

    /// <summary>
    ///     One file per (layer, context key): a shared dependency indexed under two different
    ///     leaves keeps two snapshots instead of overwriting one. The key is a 16-hex-digit hash;
    ///     the first eight digits name the file, the full value is still checked inside it.
    /// </summary>
    [Theory]
    [InlineData("/projects/mymod/mymod.pgproj", "0123456789abcdef",
        "/projects/mymod/.aetswg/indices/mymod.01234567.msgpack")]
    [InlineData("/mods/empire/empire.pgproj", "fedcba9876543210",
        "/mods/empire/.aetswg/indices/empire.fedcba98.msgpack")]
    public void GetIndexFilePath_ReturnsMsgpackUnderIndices_NamedByStemAndContextKey(
        string pgprojPath, string contextKey, string expected)
    {
        Assert.Equal(expected, ProjectIndexLocator.GetIndexFilePath(pgprojPath, contextKey));
    }

    [Fact]
    public void GetIndexFilePath_ShortKey_IsUsedWhole()
    {
        Assert.Equal("/p/m/.aetswg/indices/m.abc.msgpack",
            ProjectIndexLocator.GetIndexFilePath("/p/m/m.pgproj", "abc"));
    }

    [Fact]
    public void GetIndexFilePattern_MatchesEveryContextOfTheLayerAndNothingElse()
    {
        // The pruning glob: every keyed snapshot of THIS stem. A stem that is a prefix of another
        // ("mod" vs "mod2") must not match the other's files - the dot before the key separates.
        Assert.Equal("mod.*.msgpack", ProjectIndexLocator.GetIndexFilePattern("/p/mod/mod.pgproj"));
    }

    [Fact]
    public void GetLegacyIndexFilePath_IsTheKeylessNameEarlierVersionsWrote()
    {
        Assert.Equal("/p/m/.aetswg/indices/m.msgpack", ProjectIndexLocator.GetLegacyIndexFilePath("/p/m/m.pgproj"));
    }

    [Fact]
    public void GetAetswgDirectory_UsesForwardSlashes()
    {
        var result = ProjectIndexLocator.GetAetswgDirectory("/some/path/mod.pgproj");
        Assert.DoesNotContain('\\', result);
    }
}