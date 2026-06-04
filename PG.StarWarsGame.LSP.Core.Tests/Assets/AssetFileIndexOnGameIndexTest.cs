// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Tests.Assets;

public sealed class AssetFileIndexOnGameIndexTest
{
    // ── GameIndex.Empty default ──────────────────────────────────────────────

    [Fact]
    public void GameIndex_Empty_AssetFiles_Contains_ReturnsFalse()
    {
        Assert.False(GameIndex.Empty.AssetFiles.Contains("data/art/foo.tga"));
    }

    [Fact]
    public void GameIndex_Empty_AssetFiles_GetByExtension_IsEmpty()
    {
        Assert.Empty(GameIndex.Empty.AssetFiles.GetByExtension(".tga"));
    }

    // ── IGameIndexService.ApplyAssetFiles ────────────────────────────────────

    [Fact]
    public void ApplyAssetFiles_UpdatesCurrent()
    {
        var service = BuildService();
        var idx = new MergedAssetFileIndex(["data/art/foo.tga"]);

        service.ApplyAssetFiles(idx);

        Assert.True(service.Current.AssetFiles.Contains("data/art/foo.tga"));
    }

    [Fact]
    public void ApplyAssetFiles_FiresIndexChanged()
    {
        var service = BuildService();
        GameIndex? received = null;
        service.IndexChanged += idx => received = idx;

        service.ApplyAssetFiles(new MergedAssetFileIndex(["data/art/foo.tga"]));

        Assert.NotNull(received);
        Assert.True(received!.AssetFiles.Contains("data/art/foo.tga"));
    }

    private static IGameIndexService BuildService()
    {
        return new GameIndexService(new FileHelper(new MockFileSystem()), [],
            NullLogger<GameIndexService>.Instance);
    }
}

public sealed class MergedAssetFileIndexTest
{
    [Fact]
    public void Contains_IsCaseInsensitive()
    {
        var idx = new MergedAssetFileIndex(["data/art/textures/foo.tga"]);

        Assert.True(idx.Contains("DATA/ART/TEXTURES/FOO.TGA"));
        Assert.True(idx.Contains("data/art/textures/foo.tga"));
        Assert.False(idx.Contains("data/art/textures/bar.tga"));
    }

    [Fact]
    public void GetByExtension_FiltersByExtension()
    {
        var idx = new MergedAssetFileIndex([
            "data/art/textures/foo.tga",
            "data/art/textures/bar.dds",
            "data/art/models/baz.alo"
        ]);

        var tgas = idx.GetByExtension(".tga").ToList();
        Assert.Single(tgas);
        Assert.Contains("data/art/textures/foo.tga", tgas);

        var alos = idx.GetByExtension(".alo").ToList();
        Assert.Single(alos);
        Assert.Contains("data/art/models/baz.alo", alos);
    }

    [Fact]
    public void GetByExtension_IsCaseInsensitive()
    {
        var idx = new MergedAssetFileIndex(["data/art/textures/foo.tga"]);

        Assert.Single(idx.GetByExtension(".TGA"));
    }

    [Fact]
    public void Merge_BaselineUnionWorkspace_Deduplicates()
    {
        var baseline = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "data/art/textures/foo.tga", "data/art/models/baz.alo");
        var workspace = new[] { "data/art/textures/foo.tga", "data/audio/hit.wav" };

        var idx = MergedAssetFileIndex.Merge(baseline, workspace);

        Assert.True(idx.Contains("data/art/textures/foo.tga"));
        Assert.True(idx.Contains("data/art/models/baz.alo"));
        Assert.True(idx.Contains("data/audio/hit.wav"));
        Assert.Single(idx.GetByExtension(".tga"));
    }

    [Fact]
    public void Merge_IsPackedAsset_OnlyBaselineExclusivePaths()
    {
        var baseline = new[] { "data/art/textures/shared.tga", "data/art/models/packed_only.alo" };
        var workspace = new[] { "data/art/textures/shared.tga", "data/art/textures/loose_only.tga" };

        var idx = MergedAssetFileIndex.Merge(baseline, workspace);

        // packed-only baseline path → packed
        Assert.True(idx.IsPackedAsset("data/art/models/packed_only.alo"));
        // path present in both → workspace override wins; NOT packed
        Assert.False(idx.IsPackedAsset("data/art/textures/shared.tga"));
        // workspace-only path → not packed
        Assert.False(idx.IsPackedAsset("data/art/textures/loose_only.tga"));
    }

    [Fact]
    public void Merge_IsPackedAsset_CaseInsensitive()
    {
        var idx = MergedAssetFileIndex.Merge(
            ["data/art/textures/packed.tga"], []);

        Assert.True(idx.IsPackedAsset("DATA/ART/TEXTURES/PACKED.TGA"));
    }
}
