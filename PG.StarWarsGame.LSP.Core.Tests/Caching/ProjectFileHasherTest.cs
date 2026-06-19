// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Caching;

namespace PG.StarWarsGame.LSP.Core.Tests.Caching;

public sealed class ProjectFileHasherTest
{
    [Fact]
    public void ComputeFileHash_SameContent_ReturnsSameHash()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/a.xml", new MockFileData("hello world"u8.ToArray()));
        fs.AddFile("/b.xml", new MockFileData("hello world"u8.ToArray()));

        var h1 = ProjectFileHasher.ComputeFileHash("/a.xml", fs);
        var h2 = ProjectFileHasher.ComputeFileHash("/b.xml", fs);

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void ComputeFileHash_DifferentContent_ReturnsDifferentHash()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/a.xml", new MockFileData("hello"u8.ToArray()));
        fs.AddFile("/b.xml", new MockFileData("world"u8.ToArray()));

        var h1 = ProjectFileHasher.ComputeFileHash("/a.xml", fs);
        var h2 = ProjectFileHasher.ComputeFileHash("/b.xml", fs);

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void ComputeFileHash_ReturnsLowercaseHex()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/a.xml", new MockFileData("test"u8.ToArray()));

        var hash = ProjectFileHasher.ComputeFileHash("/a.xml", fs);

        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void ComputeProjectHash_SameInputDifferentOrder_ReturnsSameHash()
    {
        var entries1 = new[] { ("b.xml", "hashB"), ("a.xml", "hashA") };
        var entries2 = new[] { ("a.xml", "hashA"), ("b.xml", "hashB") };

        var h1 = ProjectFileHasher.ComputeProjectHash(entries1);
        var h2 = ProjectFileHasher.ComputeProjectHash(entries2);

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void ComputeProjectHash_DifferentEntries_ReturnsDifferentHash()
    {
        var entries1 = new[] { ("a.xml", "hashA") };
        var entries2 = new[] { ("a.xml", "hashX") };

        var h1 = ProjectFileHasher.ComputeProjectHash(entries1);
        var h2 = ProjectFileHasher.ComputeProjectHash(entries2);

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void ComputeProjectHash_EmptyInput_ReturnsStableHash()
    {
        var h1 = ProjectFileHasher.ComputeProjectHash([]);
        var h2 = ProjectFileHasher.ComputeProjectHash([]);

        Assert.Equal(h1, h2);
        Assert.Matches("^[0-9a-f]{64}$", h1);
    }
}