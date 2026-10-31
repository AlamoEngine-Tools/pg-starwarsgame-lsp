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
        Assert.True(Directory.Exists(path), $"Baseline folder does not exist: {path}");
    }

    // The folder, not one game's file: the server reads the file of the game each fixture's project
    // targets from it, so the foc/ workspace is checked against the FoC baseline and eaw/ against
    // the EaW one, with no download in between.
    [Theory]
    [InlineData("foc")]
    [InlineData("eaw")]
    public void BaselineLocalPath_HoldsEachGamesBaseline(string game)
    {
        var path = LspTestEnvironment.BaselineLocalPath;

        Assert.NotNull(path);
        Assert.True(File.Exists(Path.Combine(path, game, $"aet-pg-swg-lsp-{game}-baseline.aet")));
    }
}