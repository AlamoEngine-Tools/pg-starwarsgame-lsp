// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

public sealed class ModProjectDetectorTest
{
    private static readonly string Root =
        Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, "mods", "root");

    private static readonly string OtherRoot =
        Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, "mods", "other");

    [Fact]
    public void TryFind_PgprojInRoot_ReturnsTrueAndPath()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "mymod.pgproj")] = new("{}")
        });
        var detector = Build(fs);

        var found = detector.TryFind([Root], out var path);

        Assert.True(found);
        Assert.Equal(Path.Combine(Root, "mymod.pgproj"), path);
    }

    [Fact]
    public void TryFind_NoPgproj_ReturnsFalse()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(Root);
        var detector = Build(fs);

        var found = detector.TryFind([Root], out var path);

        Assert.False(found);
        Assert.Null(path);
    }

    /// <summary>
    ///     Pinned the old refusal ("Only one .pgproj is supported per workspace"). Several project
    ///     files under one root are an ordinary layout now that one server runs per project; the
    ///     two-argument overload keeps working and simply answers with the shallowest.
    /// </summary>
    [Fact]
    public void TryFind_MultiplePgproj_TheTwoArgumentOverloadNoLongerThrows()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "a.pgproj")] = new("{}"),
            [Path.Combine(Root, "b.pgproj")] = new("{}")
        });
        var detector = Build(fs);

        var found = detector.TryFind([Root], out var path);

        Assert.True(found);
        Assert.Equal(Path.Combine(Root, "a.pgproj"), path);
    }

    [Fact]
    public void TryFind_ChecksMultipleRoots()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(OtherRoot, "mymod.pgproj")] = new("{}")
        });
        fs.AddDirectory(Root);
        var detector = Build(fs);

        var found = detector.TryFind([Root, OtherRoot], out var path);

        Assert.True(found);
        Assert.Equal(Path.Combine(OtherRoot, "mymod.pgproj"), path);
    }

    [Fact]
    public void TryFind_PgprojInSubdirectory_Found()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "sub", "mymod.pgproj")] = new("{}")
        });
        var detector = Build(fs);

        var found = detector.TryFind([Root], out var path);

        Assert.True(found);
        Assert.Equal(Path.Combine(Root, "sub", "mymod.pgproj"), path);
    }

    [Fact]
    public void TryFind_MultiplePgproj_ReturnsTheShallowestAndNamesTheOthers()
    {
        // A root with several project files is an ordinary multi-project layout now that one
        // server runs per project; a client that passes no projectPath (a JetBrains host sends one
        // folder) gets the shallowest, and the others are reported rather than refused.
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "sub1", "a.pgproj")] = new("{}"),
            [Path.Combine(Root, "deep", "er", "b.pgproj")] = new("{}"),
            [Path.Combine(Root, "c.pgproj")] = new("{}")
        });
        var detector = Build(fs);

        var found = detector.TryFind([Root], out var path, out var others);

        Assert.True(found);
        Assert.Equal(Path.Combine(Root, "c.pgproj"), path);
        Assert.Equal(2, others.Count);
        Assert.DoesNotContain(path, others);
    }

    [Fact]
    public void TryFind_SameDepth_PicksByNameForDeterminism()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "zeta.pgproj")] = new("{}"),
            [Path.Combine(Root, "alpha.pgproj")] = new("{}")
        });
        var detector = Build(fs);

        detector.TryFind([Root], out var path, out var others);

        Assert.Equal(Path.Combine(Root, "alpha.pgproj"), path);
        Assert.Equal([Path.Combine(Root, "zeta.pgproj")], others);
    }

    [Fact]
    public void TryFind_SingleProject_ReportsNoOthers()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "mymod.pgproj")] = new("{}")
        });

        Build(fs).TryFind([Root], out _, out var others);

        Assert.Empty(others);
    }

    private static ModProjectDetector Build(MockFileSystem fs)
    {
        return new ModProjectDetector(new FileHelper(fs), NullLogger<ModProjectDetector>.Instance);
    }
}