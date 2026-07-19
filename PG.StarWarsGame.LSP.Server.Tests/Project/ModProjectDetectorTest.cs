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

    private static ModProjectDetector Build(MockFileSystem fs)
    {
        return new ModProjectDetector(new FileHelper(fs), NullLogger<ModProjectDetector>.Instance);
    }
}