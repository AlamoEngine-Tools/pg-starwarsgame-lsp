// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Tests.Startup;

/// <summary>
///     The game is read before the project loads, so the baseline download can run beside the
///     schema's. It is a hint: the project load decides, and corrects a wrong guess.
/// </summary>
public sealed class TargetGameProbeTest
{
    private const string Root = "/ws";

    [Fact]
    public void Probe_DetectedProjectNamingEaw_IsEaw()
    {
        var probe = Build(new Dictionary<string, MockFileData>
        {
            [$"{Root}/mod.pgproj"] = new("""{ "name": "Mod", "game": "eaw" }""")
        });

        Assert.Equal(TargetGame.EAW, probe.Probe([Root]));
    }

    [Fact]
    public void Probe_ProjectNamedByTheClient_IsReadEvenOutsideTheRoots()
    {
        var probe = Build(new Dictionary<string, MockFileData>
        {
            ["/elsewhere/mod.pgproj"] = new("""{ "name": "Mod", "game": "eaw" }""")
        }, "/elsewhere/mod.pgproj");

        Assert.Equal(TargetGame.EAW, probe.Probe([Root]));
    }

    [Theory]
    [InlineData("""{ "name": "Mod" }""")]
    [InlineData("""{ "name": "Mod", "game": "swgb" }""")]
    [InlineData("not json")]
    public void Probe_NoUsableGame_IsFoc(string text)
    {
        var probe = Build(new Dictionary<string, MockFileData> { [$"{Root}/mod.pgproj"] = new(text) });

        Assert.Equal(TargetGame.FOC, probe.Probe([Root]));
    }

    [Fact]
    public void Probe_NoProject_IsFoc()
    {
        var probe = Build(new Dictionary<string, MockFileData> { [$"{Root}/readme.txt"] = new("x") });

        Assert.Equal(TargetGame.FOC, probe.Probe([Root]));
    }

    private static TargetGameProbe Build(Dictionary<string, MockFileData> files, string? namedProject = null)
    {
        var fileHelper = new FileHelper(new MockFileSystem(files));
        var config = new FakeLspConfigurationProvider { Current = new LspConfiguration { ProjectPath = namedProject } };
        return new TargetGameProbe(
            new ModProjectDetector(fileHelper, NullLogger<ModProjectDetector>.Instance), config, fileHelper,
            NullLogger<TargetGameProbe>.Instance);
    }
}
