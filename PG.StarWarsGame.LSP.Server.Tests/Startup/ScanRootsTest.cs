// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Tests.Startup;

/// <summary>
///     What a server scans: its own project when it was given one, never the window's other folders
///     (#167). A window with two projects sends both folders to both servers; scanning them put one
///     project's metafiles, icons and encyclopedia layout into the other's server.
/// </summary>
public sealed class ScanRootsTest
{
    private static readonly string[] BothFolders = ["d:/mods/eaw", "d:/mods/foc"];

    [Fact]
    public void AGivenProject_ScansItsOwnRootOnly()
    {
        var roots = ScanRoots.Compute(BothFolders, null, "d:/mods/foc", true);

        Assert.Equal(new[] { "d:/mods/foc" }, roots.ToArray());
    }

    [Fact]
    public void WithoutAProject_ScansTheWindowsFolders()
    {
        var roots = ScanRoots.Compute(BothFolders, null, null, false);

        Assert.Equal(BothFolders, roots.ToArray());
    }

    [Fact]
    public void WithoutAProject_AddsTheConfiguredRootOnce()
    {
        var roots = ScanRoots.Compute(BothFolders, null, "D:/MODS/EAW", false);

        Assert.Equal(BothFolders, roots.ToArray());
    }

    [Fact]
    public void WithoutFolders_FallsBackToTheRootUri()
    {
        var roots = ScanRoots.Compute([], "d:/mods/eaw", null, false);

        Assert.Equal(new[] { "d:/mods/eaw" }, roots.ToArray());
    }
}