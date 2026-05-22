// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
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
        ".pg-swg-lsp", "schema");

    private static SchemaHttpCache BuildCache(MockFileSystem fs)
    {
        return new SchemaHttpCache(fs, NullLogger<SchemaHttpCache>.Instance);
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
}