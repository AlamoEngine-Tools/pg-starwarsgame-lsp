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
    private const string Key = "0123456789abcdef";
    private const string OtherKey = "fedcba9876543210";
    private static readonly string PgprojPath = "/projects/mymod/mymod.pgproj";
    private static readonly string IndexFile = "/projects/mymod/.aetswg/indices/mymod.01234567.msgpack";
    private static readonly string OtherIndexFile = "/projects/mymod/.aetswg/indices/mymod.fedcba98.msgpack";
    private static readonly string LegacyIndexFile = "/projects/mymod/.aetswg/indices/mymod.msgpack";
    private static readonly string IndicesDir = "/projects/mymod/.aetswg/indices";
    private static readonly string AetswgDir = "/projects/mymod/.aetswg";

    private static ProjectIndexCache Build(MockFileSystem fs, ICrossProcessLock? processLock = null)
    {
        return new ProjectIndexCache(new FileHelper(fs), processLock ?? new NullCrossProcessLock(),
            NullLogger<ProjectIndexCache>.Instance);
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

        var result = cache.TryLoad(PgprojPath, Key);

        Assert.Null(result);
    }

    [Fact]
    public void TryLoad_ValidSnapshot_ReturnsSnapshot()
    {
        var snapshot = MakeSnapshot("deadbeef");
        var bytes = ProjectIndexSerializer.Serialize(snapshot);
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [IndexFile] = new(bytes) });
        var cache = Build(fs);

        var result = cache.TryLoad(PgprojPath, Key);

        Assert.NotNull(result);
        Assert.Equal("deadbeef", result.OverallHash);
    }

    /// <summary>
    ///     The context key is part of the file name, so a snapshot written under another context
    ///     is simply another file - never a hit, never overwritten.
    /// </summary>
    [Fact]
    public void TryLoad_SnapshotOfAnotherContext_ReturnsNull()
    {
        var bytes = ProjectIndexSerializer.Serialize(MakeSnapshot("other"));
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [OtherIndexFile] = new(bytes) });
        var cache = Build(fs);

        Assert.Null(cache.TryLoad(PgprojPath, Key));
    }

    [Fact]
    public void TryLoad_CorruptFile_ReturnsNull()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [IndexFile] = new([0x00, 0x01, 0x02]) });
        var cache = Build(fs);

        var result = cache.TryLoad(PgprojPath, Key);

        Assert.Null(result);
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_WritesSnapshotToTheContextsPath()
    {
        var fs = new MockFileSystem();
        var cache = Build(fs);
        var snapshot = MakeSnapshot("savehash");

        cache.Save(PgprojPath, Key, snapshot);

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

        cache.Save(PgprojPath, Key, MakeSnapshot());

        Assert.True(fs.Directory.Exists(IndicesDir));
    }

    [Fact]
    public void Save_OverwritesExistingSnapshotOfTheSameContext()
    {
        var fs = new MockFileSystem();
        var cache = Build(fs);
        cache.Save(PgprojPath, Key, MakeSnapshot("v1"));
        cache.Save(PgprojPath, Key, MakeSnapshot("v2"));

        var loaded = ProjectIndexSerializer.Deserialize(fs.File.ReadAllBytes(IndexFile));
        Assert.Equal("v2", loaded!.OverallHash);
        Assert.Single(fs.Directory.GetFiles(IndicesDir));
    }

    /// <summary>
    ///     MEASURED 2026-10-04: one file per layer meant that a dependency opened under two
    ///     different leaves had its snapshot rewritten on every start, and re-parsed every time.
    ///     Two contexts, two files, both kept.
    /// </summary>
    [Fact]
    public void Save_TwoContexts_KeepTwoFiles()
    {
        var fs = new MockFileSystem();
        var cache = Build(fs);

        cache.Save(PgprojPath, Key, MakeSnapshot("alone"));
        cache.Save(PgprojPath, OtherKey, MakeSnapshot("under-a-leaf"));

        Assert.Equal("alone", cache.TryLoad(PgprojPath, Key)!.OverallHash);
        Assert.Equal("under-a-leaf", cache.TryLoad(PgprojPath, OtherKey)!.OverallHash);
    }

    [Fact]
    public void Save_KeepsOnlyTheNewestEightContextsOfALayer()
    {
        // Bounded so a project opened under many short-lived contexts does not collect snapshots
        // forever; the newest by write time survive, which are the contexts still in use. Eight,
        // because EaWX core already has four distinct contexts (MEASURED 2026-10-04) and a cap it
        // sits on would bring the re-parse back with the next leaf.
        var fs = new MockFileSystem();
        var cache = Build(fs);
        var t0 = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var keys = "abcdefghi".Select(c => new string(c, 8) + "00000000").ToArray();
        for (var i = 0; i < keys.Length; i++)
        {
            cache.Save(PgprojPath, keys[i], MakeSnapshot(keys[i]));
            fs.File.SetLastWriteTimeUtc(ProjectIndexLocator.GetIndexFilePath(PgprojPath, keys[i]), t0.AddMinutes(i));
        }

        var remaining = fs.Directory.GetFiles(IndicesDir).Select(Path.GetFileName).Order().ToArray();

        // The oldest ("aaaaaaaa") is gone; the other eight stay.
        Assert.Equal(keys.Skip(1).Select(k => "mymod." + k[..8] + ".msgpack"), remaining);
    }

    [Fact]
    public void Save_PrunesOnlyThisLayersFiles()
    {
        // "mod" is a prefix of "mymod"; the other project's snapshots in a shared indices directory
        // are not this layer's business. (Two pgproj in one directory is unusual but legal.)
        var others = Enumerable.Range(1, 9).ToDictionary(
            i => "/projects/mymod/.aetswg/indices/mod." + new string((char)('0' + i), 8) + ".msgpack",
            i => new MockFileData([(byte)i]));
        var fs = new MockFileSystem(others);
        var cache = Build(fs);

        cache.Save(PgprojPath, Key, MakeSnapshot());

        Assert.Equal(10, fs.Directory.GetFiles(IndicesDir).Length);
    }

    /// <summary>
    ///     Earlier versions wrote <c>mymod.msgpack</c> with no key. Nothing reads it any more, so
    ///     the first save under the new scheme removes it rather than leaving a dead file that
    ///     looks like a cache.
    /// </summary>
    [Fact]
    public void Save_RemovesTheLegacyKeylessSnapshot()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [LegacyIndexFile] = new(ProjectIndexSerializer.Serialize(MakeSnapshot("old"))) });
        var cache = Build(fs);

        cache.Save(PgprojPath, Key, MakeSnapshot("new"));

        Assert.False(fs.File.Exists(LegacyIndexFile));
        Assert.True(fs.File.Exists(IndexFile));
    }

    /// <summary>
    ///     Another server may hold the snapshot open for its own scan while this one saves, and on
    ///     Windows the move over an open file fails. The snapshot is a start-up accelerator; the
    ///     scan that produced it is already complete, so a failed save is logged and skipped,
    ///     never thrown into the indexer.
    /// </summary>
    [Fact]
    public void Save_WhenTheTargetCannotBeReplaced_DoesNotThrow()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [IndexFile] = new(ProjectIndexSerializer.Serialize(MakeSnapshot("held-open")))
                { Attributes = FileAttributes.ReadOnly }
        });
        var cache = Build(fs);

        var ex = Record.Exception(() => cache.Save(PgprojPath, Key, MakeSnapshot("new")));

        Assert.Null(ex);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        var fs = new MockFileSystem();
        var cache = Build(fs);

        cache.Save(PgprojPath, Key, MakeSnapshot());

        Assert.DoesNotContain(fs.Directory.GetFiles(IndicesDir), f => f.EndsWith(".tmp", StringComparison.Ordinal));
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
    public void EnsureGitHygiene_EverythingAlreadyListed_ChangesNothing()
    {
        var existing = "# custom\nindices/\nbones/\n";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [AetswgDir + "/.gitignore"] = new(existing) });
        var cache = Build(fs);

        cache.EnsureGitHygiene(PgprojPath);

        Assert.Equal(existing, fs.File.ReadAllText(AetswgDir + "/.gitignore"));
    }

    /// <summary>
    ///     Write-if-absent was enough while <c>indices/</c> was the only generated directory, but
    ///     it silently skips every project set up before a new cache was added - so the bone
    ///     snapshots would have been committed by everyone who had already opened the project. The
    ///     author's own lines are kept; only what is missing is appended.
    /// </summary>
    [Fact]
    public void EnsureGitHygiene_ExistingFileMissingAnEntry_AppendsItAndKeepsTheRest()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [AetswgDir + "/.gitignore"] = new("# custom\nindices/\n") });
        var cache = Build(fs);

        cache.EnsureGitHygiene(PgprojPath);

        var content = fs.File.ReadAllText(AetswgDir + "/.gitignore");
        Assert.Contains("# custom", content);
        Assert.Contains("indices/", content);
        Assert.Contains("bones/", content);
        // Appended once, not duplicated.
        Assert.Single(content.Split('\n').Where(l => l.Trim() == "indices/"));
    }

    [Fact]
    public void EnsureGitHygiene_RunTwice_DoesNotAccumulateDuplicates()
    {
        var fs = new MockFileSystem();
        var cache = Build(fs);

        cache.EnsureGitHygiene(PgprojPath);
        cache.EnsureGitHygiene(PgprojPath);

        var lines = fs.File.ReadAllText(AetswgDir + "/.gitignore").Split('\n');
        Assert.Single(lines.Where(l => l.Trim() == "bones/"));
    }

    /// <summary>
    ///     Several servers start together on one dependency now (one process per open project),
    ///     and each appends to the same <c>.gitignore</c>. The read-compare-write runs under the
    ///     cross-process lock, keyed by the directory, so two appenders cannot both read "missing"
    ///     and both append.
    /// </summary>
    [Fact]
    public void EnsureGitHygiene_HoldsTheCrossProcessLockWhileWriting()
    {
        var fs = new MockFileSystem();
        var recording = new RecordingLock(fs, AetswgDir + "/.gitignore");
        var cache = Build(fs, recording);

        cache.EnsureGitHygiene(PgprojPath);

        Assert.Single(recording.Acquired);
        Assert.True(recording.FileWrittenWhileHeld, "the .gitignore was written outside the lock");
    }

    [Fact]
    public void Save_HoldsTheCrossProcessLockWhilePruning()
    {
        var fs = new MockFileSystem();
        var recording = new RecordingLock(fs, IndexFile);
        var cache = Build(fs, recording);

        cache.Save(PgprojPath, Key, MakeSnapshot());

        Assert.NotEmpty(recording.Acquired);
        Assert.True(recording.FileWrittenWhileHeld, "the snapshot was written outside the lock");
    }

    /// <summary>
    ///     A lock that notes whether the watched file came into existence between Acquire and
    ///     Dispose - the only observable "was the write inside the lock" a unit test has.
    /// </summary>
    private sealed class RecordingLock(MockFileSystem fs, string watchedFile) : ICrossProcessLock
    {
        public readonly List<string> Acquired = [];
        public bool FileWrittenWhileHeld { get; private set; }

        public IDisposable Acquire(string name)
        {
            Acquired.Add(name);
            var existedBefore = fs.File.Exists(watchedFile);
            var contentBefore = existedBefore ? fs.File.ReadAllBytes(watchedFile) : [];
            return new Release(() =>
            {
                var existsNow = fs.File.Exists(watchedFile);
                if (existsNow && (!existedBefore || !contentBefore.SequenceEqual(fs.File.ReadAllBytes(watchedFile))))
                    FileWrittenWhileHeld = true;
            });
        }

        private sealed class Release(Action onDispose) : IDisposable
        {
            public void Dispose()
            {
                onDispose();
            }
        }
    }
}