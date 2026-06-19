// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Caching;

namespace PG.StarWarsGame.LSP.Server.Tests.Caching;

public sealed class ProjectIndexCacheTest
{
    private static readonly string PgprojPath = "/projects/mymod/mymod.pgproj";
    private static readonly string IndexFile = "/projects/mymod/.aetswg/indices/mymod.msgpack";
    private static readonly string AetswgDir = "/projects/mymod/.aetswg";

    private static ProjectIndexCache Build(MockFileSystem fs)
    {
        return new ProjectIndexCache(new FileHelper(fs), NullLogger<ProjectIndexCache>.Instance);
    }

    private static ProjectIndexSnapshot MakeSnapshot(string overallHash = "abc123")
    {
        return new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = overallHash,
            DependencyHashes = [],
            Files = []
        };
    }

    // ── TryLoad ──────────────────────────────────────────────────────────────

    [Fact]
    public void TryLoad_IndexFileMissing_ReturnsNull()
    {
        var cache = Build(new MockFileSystem());

        var result = cache.TryLoad(PgprojPath);

        Assert.Null(result);
    }

    [Fact]
    public void TryLoad_ValidSnapshot_ReturnsSnapshot()
    {
        var snapshot = MakeSnapshot("deadbeef");
        var bytes = ProjectIndexSerializer.Serialize(snapshot);
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [IndexFile] = new(bytes) });
        var cache = Build(fs);

        var result = cache.TryLoad(PgprojPath);

        Assert.NotNull(result);
        Assert.Equal("deadbeef", result.OverallHash);
    }

    [Fact]
    public void TryLoad_CorruptFile_ReturnsNull()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [IndexFile] = new([0x00, 0x01, 0x02]) });
        var cache = Build(fs);

        var result = cache.TryLoad(PgprojPath);

        Assert.Null(result);
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_WritesSnapshotToCorrectPath()
    {
        var fs = new MockFileSystem();
        var cache = Build(fs);
        var snapshot = MakeSnapshot("savehash");

        cache.Save(PgprojPath, snapshot);

        Assert.True(fs.File.Exists(IndexFile));
        var loaded = ProjectIndexSerializer.Deserialize(fs.File.ReadAllBytes(IndexFile));
        Assert.NotNull(loaded);
        Assert.Equal("savehash", loaded.OverallHash);
    }

    [Fact]
    public void Save_CreatesIndicesDirectory()
    {
        var fs = new MockFileSystem();
        var cache = Build(fs);

        cache.Save(PgprojPath, MakeSnapshot());

        Assert.True(fs.Directory.Exists("/projects/mymod/.aetswg/indices"));
    }

    [Fact]
    public void Save_OverwritesExistingSnapshot()
    {
        var fs = new MockFileSystem();
        var cache = Build(fs);
        cache.Save(PgprojPath, MakeSnapshot("v1"));
        cache.Save(PgprojPath, MakeSnapshot("v2"));

        var loaded = ProjectIndexSerializer.Deserialize(fs.File.ReadAllBytes(IndexFile));
        Assert.Equal("v2", loaded!.OverallHash);
    }

    // ── EnsureGitHygiene ────────────────────────────────────────────────────

    [Fact]
    public void EnsureGitHygiene_CreatesGitignoreAndGitattributes()
    {
        var fs = new MockFileSystem();
        var cache = Build(fs);

        cache.EnsureGitHygiene(PgprojPath);

        Assert.True(fs.File.Exists(AetswgDir + "/.gitignore"));
        Assert.True(fs.File.Exists(AetswgDir + "/.gitattributes"));
    }

    [Fact]
    public void EnsureGitHygiene_GitignoreExcludes_IndicesDirectory()
    {
        var fs = new MockFileSystem();
        var cache = Build(fs);

        cache.EnsureGitHygiene(PgprojPath);

        var content = fs.File.ReadAllText(AetswgDir + "/.gitignore");
        Assert.Contains("indices/", content);
    }

    [Fact]
    public void EnsureGitHygiene_Idempotent_DoesNotOverwriteExisting()
    {
        var existing = "# custom\nindices/\n";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [AetswgDir + "/.gitignore"] = new(existing) });
        var cache = Build(fs);

        cache.EnsureGitHygiene(PgprojPath);

        Assert.Equal(existing, fs.File.ReadAllText(AetswgDir + "/.gitignore"));
    }
}