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

    [Fact]
    public void TryFind_MultiplePgproj_ThrowsClearException()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "a.pgproj")] = new("{}"),
            [Path.Combine(Root, "b.pgproj")] = new("{}")
        });
        var detector = Build(fs);

        var ex = Assert.Throws<ModProjectLoadException>(() => detector.TryFind([Root], out _));

        Assert.Contains("a.pgproj", ex.Message);
        Assert.Contains("b.pgproj", ex.Message);
        Assert.Contains("multiple", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public void TryFind_MultiplePgprojInSubtree_ThrowsClearException()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "sub1", "a.pgproj")] = new("{}"),
            [Path.Combine(Root, "sub2", "b.pgproj")] = new("{}")
        });
        var detector = Build(fs);

        var ex = Assert.Throws<ModProjectLoadException>(() => detector.TryFind([Root], out _));

        Assert.Contains(Root, ex.Message);
    }

    [Fact]
    public void FindAll_TwoRootsEachWithOnePgproj_ReturnsBoth()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "a.pgproj")] = new("{}"),
            [Path.Combine(OtherRoot, "b.pgproj")] = new("{}")
        });
        var detector = Build(fs);

        var found = detector.FindAll([Root, OtherRoot]);

        Assert.Equal(2, found.Count);
        Assert.Contains(Path.Combine(Root, "a.pgproj"), found);
        Assert.Contains(Path.Combine(OtherRoot, "b.pgproj"), found);
    }

    [Fact]
    public void FindAll_SameProjectReachableFromOverlappingRoots_ReturnsItOnce()
    {
        // ComputeScanRoots always appends the configured workspaceRoot (the game data directory),
        // which is frequently a subdirectory of a VS Code workspace folder - so the same .pgproj is
        // routinely discovered twice and must not become two projects.
        var nested = Path.Combine(Root, "data");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(nested, "mymod.pgproj")] = new("{}")
        });
        var detector = Build(fs);

        var found = detector.FindAll([Root, nested]);

        Assert.Equal(Path.Combine(nested, "mymod.pgproj"), Assert.Single(found));
    }

    [Fact]
    public void FindAll_NoPgproj_ReturnsEmpty()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(Root);
        var detector = Build(fs);

        Assert.Empty(detector.FindAll([Root]));
    }

    [Fact]
    public void FindAll_MultiplePgprojInOneRoot_StillThrows()
    {
        // Deliberate: one folder means one project. Two mods are expressed as two workspace folders.
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "a.pgproj")] = new("{}"),
            [Path.Combine(Root, "b.pgproj")] = new("{}")
        });
        var detector = Build(fs);

        Assert.Throws<ModProjectLoadException>(() => detector.FindAll([Root]));
    }

    [Fact]
    public void FindAll_MissingRootDirectory_IsSkipped()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(OtherRoot, "b.pgproj")] = new("{}")
        });
        var detector = Build(fs);

        var found = detector.FindAll([Path.Combine(Root, "does", "not", "exist"), OtherRoot]);

        Assert.Equal(Path.Combine(OtherRoot, "b.pgproj"), Assert.Single(found));
    }

    // Returned as the interface so the TryFind default implementation is in scope.
    private static IModProjectDetector Build(MockFileSystem fs)
    {
        return new ModProjectDetector(new FileHelper(fs), NullLogger<ModProjectDetector>.Instance);
    }
}