// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Caching;

namespace PG.StarWarsGame.LSP.Server.Tests.Caching;

/// <summary>
///     The bone catalog was re-parsed from every <c>.alo</c> under every asset root on EVERY server
///     start - the constant floor under an otherwise warm scan, ~25s on the cold EaWX run. These
///     pin the snapshot that removes it, and - more importantly - the conditions under which it
///     must NOT be believed, because a stale skeleton is served silently and reads as a model that
///     lost its bones.
/// </summary>
public sealed class ModelBoneCatalogCacheTest
{
    private const string PgprojPath = "/projects/mymod/mymod.pgproj";
    private const string BonesFile = "/projects/mymod/.aetswg/bones/mymod.msgpack";

    private static ModelBoneCatalogCache Build(MockFileSystem fs)
    {
        return new ModelBoneCatalogCache(new FileHelper(fs), NullLogger<ModelBoneCatalogCache>.Instance);
    }

    private static Dictionary<string, ModelCatalogEntry> Bones()
    {
        return new Dictionary<string, ModelCatalogEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["ev_speeder.alo"] = new(["root", "bone_turret"], ["hull.tga", "glow.dds"]),
            // A model that names NO textures - an empty list is a real answer, and round-tripping
            // it is what stops the validator reopening the file to rediscover that.
            ["uv_walker.alo"] = new(["root"], [])
        };
    }

    // ── round trip ───────────────────────────────────────────────────────────

    [Fact]
    public void Save_ThenTryLoad_WithSameFingerprint_ReturnsTheBones()
    {
        var fs = new MockFileSystem();
        var cache = Build(fs);
        cache.Save(PgprojPath, "fp-1", Bones());

        var loaded = cache.TryLoad(PgprojPath, "fp-1");

        Assert.NotNull(loaded);
        Assert.Equal(["root", "bone_turret"], loaded!["ev_speeder.alo"].Bones);
        Assert.Equal(["root"], loaded["uv_walker.alo"].Bones);
        Assert.Equal(["hull.tga", "glow.dds"], loaded["ev_speeder.alo"].Textures);
        Assert.Empty(loaded["uv_walker.alo"].Textures);
    }

    [Fact]
    public void TryLoad_KeysAreCaseInsensitive_LikeTheModelBoneKey()
    {
        // Every other consumer of a model key folds case; a snapshot that came back case-SENSITIVE
        // would answer for "ev_speeder.alo" and miss "EV_Speeder.alo", which is how XML spells it.
        var fs = new MockFileSystem();
        var cache = Build(fs);
        cache.Save(PgprojPath, "fp-1", Bones());

        var loaded = cache.TryLoad(PgprojPath, "fp-1");

        Assert.True(loaded!.ContainsKey("EV_SPEEDER.ALO"));
    }

    [Fact]
    public void TryLoad_NothingSaved_ReturnsNull()
    {
        Assert.Null(Build(new MockFileSystem()).TryLoad(PgprojPath, "fp-1"));
    }

    // ── the staleness gates ──────────────────────────────────────────────────

    [Fact]
    public void TryLoad_FingerprintDiffers_ReturnsNull()
    {
        // A model was added, removed, or rewritten. The whole point: serving the old skeleton would
        // silently answer boneName completion with bones the file no longer has.
        var fs = new MockFileSystem();
        var cache = Build(fs);
        cache.Save(PgprojPath, "fp-1", Bones());

        Assert.Null(cache.TryLoad(PgprojPath, "fp-2"));
    }

    [Fact]
    public void TryLoad_SnapshotFromAnOlderReader_ReturnsNull()
    {
        // The fingerprint covers the FILES, not the code that reads them. A change to the ALO
        // reader or to what counts as a bone has to invalidate too, or every existing snapshot
        // replays the old reader's output forever - the same trap ProjectIndexSnapshot documents.
        var stale = new ModelBoneCatalogSnapshot
        {
            SchemaVersion = ModelBoneCatalogSnapshot.CurrentSchemaVersion - 1,
            Fingerprint = "fp-1",
            Models = [new SerializedModelBones { ModelKey = "ev_speeder.alo", Bones = ["root"] }]
        };

        Assert.Null(ModelBoneCatalogSerializer.Deserialize(ModelBoneCatalogSerializer.Serialize(stale)));
    }

    [Fact]
    public void TryLoad_CorruptFile_ReturnsNullRatherThanThrowing()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [BonesFile] = new([1, 2, 3, 4, 5])
        });

        Assert.Null(Build(fs).TryLoad(PgprojPath, "fp-1"));
    }

    [Fact]
    public void Save_IsAtomic_LeavingNoTempFileBehind()
    {
        var fs = new MockFileSystem();
        Build(fs).Save(PgprojPath, "fp-1", Bones());

        Assert.True(fs.File.Exists(BonesFile));
        Assert.False(fs.File.Exists(BonesFile + ".tmp"));
    }

    [Fact]
    public void Save_OverwritesAnEarlierSnapshot()
    {
        var fs = new MockFileSystem();
        var cache = Build(fs);
        cache.Save(PgprojPath, "fp-1", Bones());
        cache.Save(PgprojPath, "fp-2", new Dictionary<string, ModelCatalogEntry> {["only.alo"] = new(["b"], [])});

        Assert.Null(cache.TryLoad(PgprojPath, "fp-1"));
        var loaded = cache.TryLoad(PgprojPath, "fp-2");
        Assert.NotNull(loaded);
        Assert.Single(loaded!);
    }

    [Fact]
    public void Save_AnEmptyCatalog_IsARealAnswerNotAMiss()
    {
        // An asset root with no models is a legitimate result, and re-walking that tree on every
        // start to rediscover "still nothing" is exactly the cost being removed here.
        var fs = new MockFileSystem();
        var cache = Build(fs);
        cache.Save(PgprojPath, "fp-1", new Dictionary<string, ModelCatalogEntry>());

        var loaded = cache.TryLoad(PgprojPath, "fp-1");

        Assert.NotNull(loaded);
        Assert.Empty(loaded!);
    }
}