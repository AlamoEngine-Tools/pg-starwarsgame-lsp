// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

public sealed class SchemaHttpCacheTest
{
    private const string TagYaml = "tags:\n  - tag: Mass\n    type: Float\n";
    private const string HardcodedYaml = "name: TestModule\nvalues:\n  - name: TEST_VALUE\n";

    private const string MetaYaml =
        "metafiles:\n  - path: data/xml/test.xml\n    metaFileType: fileRegistry\n    types:\n      - GameObjectType\n";

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".aetswg", "schema");

    private static SchemaHttpCache BuildCache(MockFileSystem fs, ICrossProcessLock? processLock = null)
    {
        return new SchemaHttpCache(new FileHelper(fs), processLock ?? new NullCrossProcessLock(),
            NullLogger<SchemaHttpCache>.Instance);
    }

    // ── cross-process safety ─────────────────────────────────────────────────
    //
    // ~/.aetswg/schema is shared by every server process on the machine, and with one server per
    // open project several of them start at the same moment. A plain WriteAllText let a second
    // process read a half-written mirror. Every write goes through the atomic helper, and the
    // whole Update runs under the cross-process lock.

    [Fact]
    public void Update_HoldsTheCrossProcessLockForTheWholeWrite()
    {
        var fs = new MockFileSystem();
        var recording = new RecordingLock(fs, Path.Combine(CacheDir, "_release"));
        var cache = BuildCache(fs, recording);

        cache.Update("{}", [("tags/Unit.yaml", TagYaml)], "hash", "v1.0.0");

        Assert.Single(recording.Acquired);
        Assert.True(recording.FileWrittenWhileHeld, "the tag was written outside the lock");
    }

    [Fact]
    public void Update_LeavesNoTemporaryFiles()
    {
        var fs = new MockFileSystem();
        var cache = BuildCache(fs);

        cache.Update("{}", [("tags/Unit.yaml", TagYaml), ("meta/m.yaml", MetaYaml)], "hash", "v1.0.0");

        var leftovers = fs.AllFiles.Where(f => f.EndsWith(".tmp", StringComparison.Ordinal)).ToArray();
        Assert.Empty(leftovers);
    }

    [Fact]
    public void RecordRelease_HoldsTheCrossProcessLock()
    {
        var fs = new MockFileSystem();
        var recording = new RecordingLock(fs, Path.Combine(CacheDir, "_index.json"));
        var cache = BuildCache(fs, recording);

        cache.RecordRelease("{}", "v1.0.0");

        Assert.Single(recording.Acquired);
        Assert.True(recording.FileWrittenWhileHeld, "the manifest was written outside the lock");
    }

    [Fact]
    public void UpdateText_HoldsTheCrossProcessLock()
    {
        var fs = new MockFileSystem();
        var recording = new RecordingLock(fs, Path.Combine(CacheDir, "lua/api.d.lua"));
        var cache = BuildCache(fs, recording);

        cache.UpdateText("lua/api.d.lua", "---@meta\n");

        Assert.Single(recording.Acquired);
        Assert.True(recording.FileWrittenWhileHeld, "the file was written outside the lock");
    }

    /// <summary>
    ///     Notes whether the watched file appeared or changed between Acquire and Dispose - the
    ///     only observable "was the write inside the lock" a unit test has.
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
                if (fs.File.Exists(watchedFile)
                    && (!existedBefore || !contentBefore.SequenceEqual(fs.File.ReadAllBytes(watchedFile))))
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

    // ── TryLoad ──────────────────────────────────────────────────────────────

    [Fact]
    public void TryLoad_ReturnsFalse_WhenChecksumFileMissing()
    {
        var fs = new MockFileSystem();
        var cache = BuildCache(fs);
        fs.AddFile(Path.Combine(CacheDir, "tags/Unit.yaml"), new MockFileData(TagYaml));

        var result = cache.TryLoad("{}", new SchemaManifest { Tags = ["tags/Unit.yaml"] }, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryLoad_ReturnsFalse_WhenBaselineHashMismatch()
    {
        var fs = new MockFileSystem();
        var cache = BuildCache(fs);
        var manifest = new SchemaManifest { Tags = ["tags/Unit.yaml"], BaselineHash = "correcthash" };
        fs.AddFile(Path.Combine(CacheDir, "_index.sha256"), new MockFileData("wronghash"));
        fs.AddFile(Path.Combine(CacheDir, "tags/Unit.yaml"), new MockFileData(TagYaml));

        var result = cache.TryLoad("{}", manifest, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryLoad_ReturnsFalse_WhenAnyFileMissing()
    {
        var fs = new MockFileSystem();
        var cache = BuildCache(fs);
        var manifest = new SchemaManifest
        {
            Tags = ["tags/Unit.yaml"],
            Hardcoded = ["hardcoded/TestModule.yaml"],
            BaselineHash = "abc123"
        };
        fs.AddFile(Path.Combine(CacheDir, "_index.sha256"), new MockFileData("abc123"));
        fs.AddFile(Path.Combine(CacheDir, "tags/Unit.yaml"), new MockFileData(TagYaml));
        // hardcoded/TestModule.yaml intentionally absent

        var result = cache.TryLoad("{}", manifest, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryLoad_ReturnsTrue_WhenBaselineHashMatches_IncludingHardcodedAndMeta()
    {
        var fs = new MockFileSystem();
        var cache = BuildCache(fs);
        const string baselineHash = "abc123";
        var manifest = new SchemaManifest
        {
            Tags = ["tags/Unit.yaml"],
            Hardcoded = ["hardcoded/TestModule.yaml"],
            Meta = ["meta/test.yaml"],
            BaselineHash = baselineHash
        };
        fs.AddFile(Path.Combine(CacheDir, "_index.sha256"), new MockFileData(baselineHash));
        fs.AddFile(Path.Combine(CacheDir, "tags/Unit.yaml"), new MockFileData(TagYaml));
        fs.AddFile(Path.Combine(CacheDir, "hardcoded/TestModule.yaml"), new MockFileData(HardcodedYaml));
        fs.AddFile(Path.Combine(CacheDir, "meta/test.yaml"), new MockFileData(MetaYaml));

        var result = cache.TryLoad("{}", manifest, out var index);

        Assert.True(result);
        Assert.NotEmpty(index.AllHardcodedSets);
        Assert.NotEmpty(index.AllMetafiles);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_WritesBaselineHashToChecksumFile()
    {
        var fs = new MockFileSystem();
        var cache = BuildCache(fs);
        const string expected = "abc123def456";

        cache.Update("{}", [("tags/Unit.yaml", TagYaml)], expected);

        var stored = fs.File.ReadAllText(Path.Combine(CacheDir, "_index.sha256")).Trim();
        Assert.Equal(expected, stored);
    }

    [Fact]
    public void Update_ComputesYamlHashWhenNoBaselineHashProvided()
    {
        var fs = new MockFileSystem();
        var cache = BuildCache(fs);

        cache.Update("{}", [("tags/Unit.yaml", TagYaml)]);

        // Checksum must exist and be non-empty; the exact value is the SHA-256 of the YAML content.
        var stored = fs.File.ReadAllText(Path.Combine(CacheDir, "_index.sha256")).Trim();
        Assert.NotEmpty(stored);
        Assert.Equal(64, stored.Length); // SHA-256 hex = 64 chars
    }

    // ── Round-trip ───────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WithBaselineHash_LoadsHardcodedAndMetaFromDisk()
    {
        var fs = new MockFileSystem();
        var cache = BuildCache(fs);
        const string baselineHash = "roundtriphash";
        var manifest = new SchemaManifest
        {
            Tags = ["tags/Unit.yaml"],
            Hardcoded = ["hardcoded/TestModule.yaml"],
            Meta = ["meta/test.yaml"],
            BaselineHash = baselineHash
        };

        cache.Update("{}", [
            ("tags/Unit.yaml", TagYaml),
            ("hardcoded/TestModule.yaml", HardcodedYaml),
            ("meta/test.yaml", MetaYaml)
        ], baselineHash);

        var result = cache.TryLoad("{}", manifest, out var index);

        Assert.True(result);
        Assert.NotEmpty(index.AllHardcodedSets);
        Assert.NotEmpty(index.AllMetafiles);
    }

    [Fact]
    public void RoundTrip_WithoutBaselineHash_IncludesHardcodedAndMetaInChecksum()
    {
        var fs = new MockFileSystem();
        var cache = BuildCache(fs);
        var manifest = new SchemaManifest
        {
            Tags = ["tags/Unit.yaml"],
            Hardcoded = ["hardcoded/TestModule.yaml"],
            Meta = ["meta/test.yaml"]
            // No BaselineHash
        };

        cache.Update("{}", [
            ("tags/Unit.yaml", TagYaml),
            ("hardcoded/TestModule.yaml", HardcodedYaml),
            ("meta/test.yaml", MetaYaml)
        ]);

        var result = cache.TryLoad("{}", manifest, out var index);

        Assert.True(result);
        Assert.NotEmpty(index.AllHardcodedSets);
        Assert.NotEmpty(index.AllMetafiles);
    }

    // ── TryLoadText / UpdateText ─────────────────────────────────────────────

    [Fact]
    public void TryLoadText_ReturnsFalse_WhenFileNotInCache()
    {
        var fs = new MockFileSystem();
        var cache = BuildCache(fs);

        var result = cache.TryLoadText("lua/api.d.lua", out var content);

        Assert.False(result);
        Assert.Empty(content);
    }

    [Fact]
    public void UpdateText_ThenTryLoadText_ReturnsStoredContent()
    {
        var fs = new MockFileSystem();
        var cache = BuildCache(fs);
        const string expected = "function Foo() end\n";

        cache.UpdateText("lua/api.d.lua", expected);
        var result = cache.TryLoadText("lua/api.d.lua", out var content);

        Assert.True(result);
        Assert.Equal(expected, content);
    }

    [Fact]
    public void UpdateText_CreatesSubdirectory_WhenRelativePathHasDirectory()
    {
        var fs = new MockFileSystem();
        var cache = BuildCache(fs);

        cache.UpdateText("nested/subdir/file.txt", "content");

        Assert.True(fs.File.Exists(Path.Combine(CacheDir, "nested", "subdir", "file.txt")));
    }

    // ── release tag ──────────────────────────────────────────────────────────

    [Fact]
    public void CachedTag_IsNull_WhenNothingWasCached()
    {
        Assert.Null(BuildCache(new MockFileSystem()).CachedTag);
    }

    [Fact]
    public void Update_WithATag_RecordsIt()
    {
        var cache = BuildCache(new MockFileSystem());

        cache.Update("{}", [("tags/Unit.yaml", TagYaml)], tag: "v2.1.0");

        Assert.Equal("v2.1.0", cache.CachedTag);
    }

    // A schema loaded from an explicit URL is not a release; keeping the old tag would let the
    // next offline start present that content as the release.
    [Fact]
    public void Update_WithoutATag_ClearsAnEarlierOne()
    {
        var cache = BuildCache(new MockFileSystem());
        cache.Update("{}", [("tags/Unit.yaml", TagYaml)], tag: "v2.1.0");

        cache.Update("{}", [("tags/Unit.yaml", TagYaml)]);

        Assert.Null(cache.CachedTag);
    }

    [Fact]
    public void TryLoadIndexJson_ReturnsWhatUpdateWrote()
    {
        var cache = BuildCache(new MockFileSystem());
        cache.Update("{\"tags\":[]}", [], tag: "v2.1.0");

        Assert.True(cache.TryLoadIndexJson(out var json));
        Assert.Equal("{\"tags\":[]}", json);
    }

    [Fact]
    public void TryLoadIndexJson_ReturnsFalse_WhenNothingWasCached()
    {
        Assert.False(BuildCache(new MockFileSystem()).TryLoadIndexJson(out _));
    }
}