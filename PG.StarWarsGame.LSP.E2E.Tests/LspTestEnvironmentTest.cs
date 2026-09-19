// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The fixtures' view of the repository. Spawns nothing - these are pure path lookups, and they
///     are the reason the suite either sees the shipped-game index or does not.
/// </summary>
public sealed class LspTestEnvironmentTest
{
    [Fact]
    public void BaselineLocalPath_FallsBackToTheRepositoryBaseline()
    {
        // Its three neighbours (schema, workspace, eaw workspace) all fall back to a path inside the
        // repository; this one only ever read the env var, so every fixture selected baselineType
        // "None" and the whole E2E suite ran with an EMPTY index. A baseline is checked in - the
        // fixtures just never looked at it.
        var path = LspTestEnvironment.BaselineLocalPath;

        Assert.NotNull(path);
        Assert.True(File.Exists(path), $"Baseline file does not exist: {path}");
        Assert.EndsWith(".aet", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BaselineLocalPath_IsTheFocBaseline_MatchingTheWorkspaceTheFixturesOpen()
    {
        // The default workspace is <repo>/foc, so the default baseline has to be the FoC one or the
        // suite would validate FoC data against the EaW index.
        var path = LspTestEnvironment.BaselineLocalPath;

        Assert.NotNull(path);
        Assert.Contains("foc", path, StringComparison.OrdinalIgnoreCase);
    }
}