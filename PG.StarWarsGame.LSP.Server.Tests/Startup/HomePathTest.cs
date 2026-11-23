// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Tests.Startup;

public sealed class HomePathTest
{
    private const string Home = @"C:\Users\someone";

    [Fact]
    public void AsTilde_NamesTheHomeDirectoryInTheStartupLine()
    {
        Assert.Equal(
            @"[LSP] PID 1 args=[--log-dir=~\.aetswg\logs\3f9c, --log-stem=My Mod]",
            HomePath.AsTilde(
                @"[LSP] PID 1 args=[--log-dir=C:\Users\someone\.aetswg\logs\3f9c, --log-stem=My Mod]", Home, true));
    }

    [Fact]
    public void AsTilde_TakesEitherSeparatorAndAnyCaseOnWindows()
    {
        Assert.Equal("[LSP] Logging to ~/.aetswg/logs/3f9c/aetswg-.log",
            HomePath.AsTilde("[LSP] Logging to c:/users/SOMEONE/.aetswg/logs/3f9c/aetswg-.log", Home, true));
    }

    [Fact]
    public void AsTilde_KeepsTheCaseApartWhereTheFileSystemDoes()
    {
        Assert.Equal("/home/Someone/mod and ~/mod",
            HomePath.AsTilde("/home/Someone/mod and /home/someone/mod", "/home/someone", false));
    }

    [Fact]
    public void AsTilde_NamesTheHomeDirectoryItself()
    {
        Assert.Equal("args=[--log-dir=~]", HomePath.AsTilde(@"args=[--log-dir=C:\Users\someone]", Home, true));
    }

    [Fact]
    public void AsTilde_LeavesAFolderThatOnlyStartsTheSameAlone()
    {
        const string line = @"C:\Users\someone-else\mod C:\Users\someone.old\mod C:\Users\someone2";

        Assert.Equal(line, HomePath.AsTilde(line, Home, true));
    }

    [Fact]
    public void AsTilde_ChangesNothingForAnUnknownHomeDirectory()
    {
        Assert.Equal(@"C:\Users\someone\x", HomePath.AsTilde(@"C:\Users\someone\x", string.Empty, true));
    }
}