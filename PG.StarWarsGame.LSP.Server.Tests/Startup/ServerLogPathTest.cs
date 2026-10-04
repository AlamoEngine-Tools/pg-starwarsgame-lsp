// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Tests.Startup;

public sealed class ServerLogPathTest
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "aet-log-base");

    [Fact]
    public void Resolve_WithoutTheOption_WritesBesideTheServer()
    {
        // Not the current directory: a server spawned by the extension inherits the editor's, and
        // the log went to the VS Code installation folder instead of anywhere the user would look.
        Assert.Equal(Path.Combine(Base, "aetswg-.log"), ServerLogPath.Resolve([], Base));
    }

    [Fact]
    public void Resolve_WithAnAbsoluteDirectory_UsesIt()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "aet-workspace");

        Assert.Equal(
            Path.Combine(workspace, "aetswg-.log"),
            ServerLogPath.Resolve([$"--log-dir={workspace}"], Base));
    }

    [Fact]
    public void Resolve_WithARelativeDirectory_AnchorsItOnTheServer_NotTheCurrentDirectory()
    {
        Assert.Equal(
            Path.Combine(Base, "logs", "aetswg-.log"),
            ServerLogPath.Resolve(["--log-dir=logs"], Base));
    }

    [Fact]
    public void Resolve_IgnoresAnEmptyDirectory()
    {
        // An unset workspace root must not turn into "the current directory" by the back door.
        Assert.Equal(Path.Combine(Base, "aetswg-.log"), ServerLogPath.Resolve(["--log-dir=   "], Base));
    }

    /// <summary>
    ///     One server per open project means several servers logging into the same directory when
    ///     a dependency is also open. The client names the project in the file name; without the
    ///     option the name is what it always was.
    /// </summary>
    [Fact]
    public void Resolve_WithALogStem_PutsItInTheFileName()
    {
        var path = ServerLogPath.Resolve(["--log-stem=core"], @"C:\srv");

        Assert.Equal("aetswg-core-.log", Path.GetFileName(path));
    }

    [Fact]
    public void Resolve_SanitizesTheStemForAFileName()
    {
        var path = ServerLogPath.Resolve([@"--log-stem=my/mod:v2"], @"C:\srv");

        Assert.Equal("aetswg-my_mod_v2-.log", Path.GetFileName(path));
    }

    [Fact]
    public void Resolve_IgnoresAnEmptyStem()
    {
        Assert.Equal("aetswg-.log", Path.GetFileName(ServerLogPath.Resolve(["--log-stem="], @"C:\srv")));
    }

    [Fact]
    public void Resolve_ReadsPastTheOtherArguments()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "aet-workspace");

        Assert.Equal(
            Path.Combine(workspace, "aetswg-.log"),
            ServerLogPath.Resolve(["--tcp=21540", $"--log-dir={workspace}", "--wait-for-debugger"], Base));
    }
}