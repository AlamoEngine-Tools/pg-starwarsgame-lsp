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

    [Fact]
    public void Resolve_ReadsPastTheOtherArguments()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "aet-workspace");

        Assert.Equal(
            Path.Combine(workspace, "aetswg-.log"),
            ServerLogPath.Resolve(["--tcp=21540", $"--log-dir={workspace}", "--wait-for-debugger"], Base));
    }
}
