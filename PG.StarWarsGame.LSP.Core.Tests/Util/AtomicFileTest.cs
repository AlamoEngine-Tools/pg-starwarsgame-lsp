// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Tests.Util;

/// <summary>
///     Every cache file under <c>~/.aetswg</c> and <c>.aetswg/</c> is written by this: serialize
///     to a sibling temp file, then move it over the target. A reader in another process sees the
///     old file or the new one, never a half-written one. Two caches had hand-rolled this and five
///     writers had not.
/// </summary>
public sealed class AtomicFileTest
{
    [Fact]
    public void WriteAllBytes_WritesTheContent()
    {
        var fs = new MockFileSystem();

        AtomicFile.WriteAllBytes(fs, "/cache/a/b.bin", [1, 2, 3]);

        Assert.Equal(new byte[] { 1, 2, 3 }, fs.File.ReadAllBytes("/cache/a/b.bin"));
    }

    [Fact]
    public void WriteAllBytes_CreatesTheDirectory()
    {
        var fs = new MockFileSystem();

        AtomicFile.WriteAllBytes(fs, "/cache/deep/er/b.bin", [1]);

        Assert.True(fs.Directory.Exists("/cache/deep/er"));
    }

    [Fact]
    public void WriteAllBytes_ReplacesAnExistingFile()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { ["/cache/b.bin"] = new([9, 9]) });

        AtomicFile.WriteAllBytes(fs, "/cache/b.bin", [1]);

        Assert.Equal(new byte[] { 1 }, fs.File.ReadAllBytes("/cache/b.bin"));
    }

    [Fact]
    public void WriteAllBytes_LeavesNoTemporaryFile()
    {
        var fs = new MockFileSystem();

        AtomicFile.WriteAllBytes(fs, "/cache/b.bin", [1]);

        Assert.Single(fs.Directory.GetFiles("/cache"));
    }

    [Fact]
    public void WriteAllText_RoundTrips()
    {
        var fs = new MockFileSystem();

        AtomicFile.WriteAllText(fs, "/cache/t.txt", "tag-1\n");

        Assert.Equal("tag-1\n", fs.File.ReadAllText("/cache/t.txt"));
        Assert.Single(fs.Directory.GetFiles("/cache"));
    }

    [Fact]
    public void WriteAllBytes_TwoWritersSameTarget_LastOneWinsAndNothingIsLeftBehind()
    {
        // Each writer uses its own temp name, so two processes racing on one file cannot clobber
        // each other's temp file; the moves serialize and the directory ends clean.
        var fs = new MockFileSystem();

        Parallel.For(0, 8, i => AtomicFile.WriteAllBytes(fs, "/cache/b.bin", [(byte)i]));

        Assert.Single(fs.Directory.GetFiles("/cache"));
        Assert.Single(fs.File.ReadAllBytes("/cache/b.bin"));
    }
}
