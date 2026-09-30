// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;

namespace PG.StarWarsGame.LSP.Core.Tests.Assets;

/// <summary>
///     A reference is usually written as a bare filename, never as the catalog's full relative
///     path, so every existence check fell through to a LINEAR SCAN of the whole catalog. MEASURED
///     on a real layered mod: one 497 KB prop file spent 24.6s of a 83s workspace sweep inside
///     that scan, because each of its models names textures that each rescan ~38,000 paths - and a
///     MISSING texture is the worst case, scanning every allowed extension and finding nothing.
///     This bucket is what makes the candidate set the handful of files sharing a name.
/// </summary>
public sealed class AssetFileIndexByFileNameTest
{
    private static MergedAssetFileIndex Index(params string[] paths)
    {
        return new MergedAssetFileIndex(paths);
    }

    [Fact]
    public void GetByFileName_ReturnsEveryPathEndingInThatName()
    {
        var index = Index(
            "data/art/textures/foo.tga",
            "data/art/models/textures/foo.tga",
            "data/art/textures/bar.tga");

        var hits = index.GetByFileName("foo.tga").ToList();

        Assert.Equal(2, hits.Count);
        Assert.Contains("data/art/textures/foo.tga", hits);
        Assert.Contains("data/art/models/textures/foo.tga", hits);
    }

    [Fact]
    public void GetByFileName_FoldsCase()
    {
        // The catalog is normalised to lowercase, but a reference is written however the modder
        // spelled it, and the shipped data and mod data disagree about casing constantly.
        var index = Index("data/art/textures/foo.tga");

        Assert.Single(index.GetByFileName("FOO.TGA"));
    }

    [Fact]
    public void GetByFileName_UnknownName_IsEmptyNotEverything()
    {
        Assert.Empty(Index("data/art/textures/foo.tga").GetByFileName("nope.tga"));
    }

    [Fact]
    public void GetByFileName_MatchesOnTheWHOLESegment()
    {
        // The bug the old "/"-anchored suffix test existed to prevent: "oo.tga" must not be
        // satisfied by "foo.tga". Bucketing by full segment keeps that guarantee by construction.
        var index = Index("data/art/textures/foo.tga");

        Assert.Empty(index.GetByFileName("oo.tga"));
    }

    [Fact]
    public void GetByFileName_APathWithNoDirectory_IsStillFound()
    {
        Assert.Single(Index("foo.tga").GetByFileName("foo.tga"));
    }

    [Fact]
    public void GetByFileName_EmptyName_IsEmpty()
    {
        Assert.Empty(Index("data/art/textures/foo.tga").GetByFileName(""));
    }

    [Fact]
    public void GetByFileName_CoversMergedWorkspaceAndBaseline()
    {
        var index = MergedAssetFileIndex.Merge(
            ["data/art/textures/packed.tga"],
            ["data/art/textures/loose.tga"]);

        Assert.Single(index.GetByFileName("packed.tga"));
        Assert.Single(index.GetByFileName("loose.tga"));
    }

    [Fact]
    public void EmptyIndex_AnswersWithoutThrowing()
    {
        Assert.Empty(EmptyAssetFileIndex.Instance.GetByFileName("foo.tga"));
    }
}
