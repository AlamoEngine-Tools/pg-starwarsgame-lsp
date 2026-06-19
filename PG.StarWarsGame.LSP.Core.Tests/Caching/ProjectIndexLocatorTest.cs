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

    [Theory]
    [InlineData("/projects/mymod/mymod.pgproj", "/projects/mymod/.aetswg/indices/mymod.msgpack")]
    [InlineData("/mods/empire/empire.pgproj", "/mods/empire/.aetswg/indices/empire.msgpack")]
    public void GetIndexFilePath_ReturnsMsgpackUnderIndices(string pgprojPath, string expected)
    {
        Assert.Equal(expected, ProjectIndexLocator.GetIndexFilePath(pgprojPath));
    }

    [Fact]
    public void GetAetswgDirectory_UsesForwardSlashes()
    {
        var result = ProjectIndexLocator.GetAetswgDirectory("/some/path/mod.pgproj");
        Assert.DoesNotContain('\\', result);
    }
}