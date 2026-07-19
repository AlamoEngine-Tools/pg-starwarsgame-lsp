// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion.Providers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion.Providers;

public sealed class AssetFileCompletionProviderTest
{
    private static readonly AssetFileCompletionProvider Provider = new();

    private static XmlTagDefinition Tag(ReferenceKind kind)
    {
        return new XmlTagDefinition
        {
            Tag = "Texture", ValueType = XmlValueType.NameReference, ReferenceKind = kind
        };
    }

    private static GameIndex IndexWith(params string[] paths)
    {
        return GameIndex.Empty with { AssetFiles = new MergedAssetFileIndex(paths) };
    }

    private static GameIndex IndexWithPacked(string[] baselinePaths, string[] workspacePaths)
    {
        return GameIndex.Empty with
        {
            AssetFiles = MergedAssetFileIndex.Merge(baselinePaths, workspacePaths)
        };
    }

    // ── CanHandle ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ReferenceKind.TextureFile)]
    [InlineData(ReferenceKind.ModelFile)]
    [InlineData(ReferenceKind.AudioFile)]
    [InlineData(ReferenceKind.MapFile)]
    public void CanHandle_AssetReferenceKinds_True(ReferenceKind kind)
    {
        Assert.True(Provider.CanHandle(Tag(kind)));
    }

    [Theory]
    [InlineData(ReferenceKind.XmlObject)]
    [InlineData(ReferenceKind.LocalisationKey)]
    [InlineData(ReferenceKind.None)]
    public void CanHandle_NonAssetReferenceKinds_False(ReferenceKind kind)
    {
        Assert.False(Provider.CanHandle(Tag(kind)));
    }

    // ── GetProposals - extension filtering by ReferenceKind ──────────────────

    [Fact]
    public void GetProposals_TextureKind_OnlyTextureExtensions()
    {
        var index = IndexWith(
            "data/art/textures/foo.tga",
            "data/art/textures/bar.dds",
            "data/art/models/baz.alo",
            "data/audio/hit.wav");

        var result = Provider.GetProposals(Tag(ReferenceKind.TextureFile), "", index);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Label == "foo.tga");
        Assert.Contains(result, p => p.Label == "bar.dds");
    }

    [Fact]
    public void GetProposals_ModelKind_OnlyAlo()
    {
        var index = IndexWith("data/art/models/baz.alo", "data/art/textures/foo.tga");

        var result = Provider.GetProposals(Tag(ReferenceKind.ModelFile), "", index);

        Assert.Single(result);
        Assert.Equal("baz.alo", result[0].Label);
    }

    [Fact]
    public void GetProposals_Label_IsFilenameOnly_DetailIsFullPath()
    {
        var index = IndexWith("data/art/textures/foo.tga");

        var result = Provider.GetProposals(Tag(ReferenceKind.TextureFile), "", index);

        Assert.Single(result);
        Assert.Equal("foo.tga", result[0].Label);
        Assert.Equal("data/art/textures/foo.tga", result[0].Detail);
    }

    [Fact]
    public void GetProposals_SameFilenameInDifferentDirectories_BothIncluded()
    {
        var index = IndexWith(
            "data/art/textures/unit.tga",
            "data/art/textures/patch/unit.tga");

        var result = Provider.GetProposals(Tag(ReferenceKind.TextureFile), "", index);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Count(p => p.Label == "unit.tga"));
    }

    // ── GetProposals - prefix filtering ─────────────────────────────────────

    [Fact]
    public void GetProposals_PrefixMatchesFilename_CaseInsensitive()
    {
        var index = IndexWith("data/art/textures/foo.tga", "data/art/textures/bar.tga");

        var result = Provider.GetProposals(Tag(ReferenceKind.TextureFile), "FO", index);

        Assert.Single(result);
        Assert.Equal("foo.tga", result[0].Label);
    }

    [Fact]
    public void GetProposals_PrefixMatchesFullPath_LabelStillFilenameOnly()
    {
        var index = IndexWith("data/art/textures/foo.tga", "data/audio/foo.wav");

        var result = Provider.GetProposals(Tag(ReferenceKind.TextureFile), "data/art", index);

        Assert.Single(result);
        Assert.Equal("foo.tga", result[0].Label);
        Assert.Equal("data/art/textures/foo.tga", result[0].Detail);
    }

    [Fact]
    public void GetProposals_EmptyIndex_ReturnsEmpty()
    {
        var result = Provider.GetProposals(Tag(ReferenceKind.TextureFile), "", GameIndex.Empty);
        Assert.Empty(result);
    }

    // ── packed / workspace distinction ────────────────────────────────────────

    [Fact]
    public void GetProposals_PackedBaselineAsset_DescriptionIsPacked()
    {
        var index = IndexWithPacked(
            ["data/art/textures/foo.tga"],
            []);

        var result = Provider.GetProposals(Tag(ReferenceKind.TextureFile), "", index);

        Assert.Single(result);
        Assert.Equal("packed", result[0].Description);
    }

    [Fact]
    public void GetProposals_WorkspaceLooseAsset_DescriptionIsNull()
    {
        var index = IndexWithPacked(
            [],
            ["data/art/textures/foo.tga"]);

        var result = Provider.GetProposals(Tag(ReferenceKind.TextureFile), "", index);

        Assert.Single(result);
        Assert.Null(result[0].Description);
    }

    [Fact]
    public void GetProposals_MixedAssets_PackedAndWorkspaceDistinguished()
    {
        var index = IndexWithPacked(
            ["data/art/textures/packed.tga"],
            ["data/art/textures/loose.tga"]);

        var result = Provider.GetProposals(Tag(ReferenceKind.TextureFile), "", index);

        Assert.Equal(2, result.Count);
        Assert.Equal("packed", result.First(p => p.Label == "packed.tga").Description);
        Assert.Null(result.First(p => p.Label == "loose.tga").Description);
    }
}