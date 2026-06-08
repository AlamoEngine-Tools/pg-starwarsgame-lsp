// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class HoverUtilityTest
{
    [Fact]
    public void Resolve_ExactLocaleMatch_ReturnsIt()
    {
        var desc = new Dictionary<string, string> { ["de"] = "Deutsch", ["en"] = "English" };
        Assert.Equal("Deutsch", HoverUtility.Resolve(desc, "de"));
    }

    [Fact]
    public void Resolve_LocaleIsEn_ReturnsEn()
    {
        var desc = new Dictionary<string, string> { ["en"] = "English" };
        Assert.Equal("English", HoverUtility.Resolve(desc, "en"));
    }

    [Fact]
    public void Resolve_LocaleMissing_FallsBackToEn()
    {
        var desc = new Dictionary<string, string> { ["en"] = "English" };
        Assert.Equal("English", HoverUtility.Resolve(desc, "fr"));
    }

    [Fact]
    public void Resolve_NoDescriptions_ReturnsPrHintMessage()
    {
        var result = HoverUtility.Resolve(new Dictionary<string, string>(), "en");
        // Should mention contributing / PR
        Assert.Contains("PR", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_NeitherLocaleNorEn_ReturnsPrHintMessage()
    {
        var desc = new Dictionary<string, string> { ["ja"] = "日本語" };
        var result = HoverUtility.Resolve(desc, "fr");
        Assert.Contains("PR", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_EnFallbackIsCaseInsensitive()
    {
        // "EN" key should still match the "en" fallback path
        var desc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = "English" };
        Assert.Equal("English", HoverUtility.Resolve(desc, "fr"));
    }

    // ── BuildReferenceHover ───────────────────────────────────────────────────

    private static GameObjectTypeDefinition MakeType(string typeName, string descEn = "A type.")
    {
        return new GameObjectTypeDefinition
        {
            TypeName = typeName,
            Description = new Dictionary<string, string> { ["en"] = descEn }
        };
    }

    private static GameReference MakeRef(string id, int line, int col, int len)
    {
        return new GameReference(id, GameSymbolKind.XmlObject, null, "file:///test.xml", line, col, len);
    }

    [Fact]
    public void BuildReferenceHover_ContainsTypeNameAndSymbolId()
    {
        var hover = HoverUtility.BuildReferenceHover(
            MakeType("SpaceUnit"), "Fighter_Mk2", MakeRef("Fighter_Mk2", 2, 14, 11), "en");
        var md = hover.Contents.MarkupContent!.Value;
        Assert.Contains("SpaceUnit", md);
        Assert.Contains("Fighter_Mk2", md);
    }

    [Fact]
    public void BuildReferenceHover_ContainsTypeDescription()
    {
        var hover = HoverUtility.BuildReferenceHover(
            MakeType("Faction", "A playable faction."), "EMPIRE", MakeRef("EMPIRE", 0, 13, 6), "en");
        Assert.Contains("A playable faction.", hover.Contents.MarkupContent!.Value);
    }

    [Fact]
    public void BuildReferenceHover_RangeCoversReferenceToken()
    {
        var hover = HoverUtility.BuildReferenceHover(
            MakeType("SpaceUnit"), "Fighter_Mk2", MakeRef("Fighter_Mk2", 2, 14, 11), "en");
        Assert.Equal(2, hover.Range!.Start.Line);
        Assert.Equal(14, hover.Range.Start.Character);
        Assert.Equal(2, hover.Range.End.Line);
        Assert.Equal(25, hover.Range.End.Character); // 14 + 11
    }

    [Fact]
    public void BuildReferenceHover_IsMarkdown()
    {
        var hover = HoverUtility.BuildReferenceHover(
            MakeType("SpaceUnit"), "X_Wing", MakeRef("X_Wing", 0, 0, 6), "en");
        Assert.Equal(MarkupKind.Markdown,
            hover.Contents.MarkupContent!.Kind);
    }

    // ── MegArchiveOrigin section ───────────────────────────────────────────────

    [Fact]
    public void BuildReferenceHover_MegArchiveOrigin_ContainsPackedSection()
    {
        var origin = new MegArchiveOrigin(@"C:\game\FoC_Art.meg", "DATA/ART/TEXTURES/FOO.TGA", null, null);
        var hover = HoverUtility.BuildReferenceHover(
            MakeType("SpaceUnit"), "Fighter_Mk2", MakeRef("Fighter_Mk2", 2, 14, 11), "en", origin);
        var md = hover.Contents.MarkupContent!.Value;
        Assert.Contains("FoC_Art.meg", md);
        Assert.Contains("DATA/ART/TEXTURES/FOO.TGA", md);
        Assert.Contains("📦", md);
    }

    [Fact]
    public void BuildReferenceHover_NullOrigin_NoPackedSection()
    {
        var hover = HoverUtility.BuildReferenceHover(
            MakeType("SpaceUnit"), "Fighter_Mk2", MakeRef("Fighter_Mk2", 2, 14, 11), "en");
        var md = hover.Contents.MarkupContent!.Value;
        Assert.DoesNotContain("📦", md);
    }

    [Fact]
    public void BuildReferenceHover_FileOrigin_NoPackedSection()
    {
        var origin = new FileOrigin("file:///game/data/foo.xml", 10, 5);
        var hover = HoverUtility.BuildReferenceHover(
            MakeType("SpaceUnit"), "Fighter_Mk2", MakeRef("Fighter_Mk2", 2, 14, 11), "en", origin);
        var md = hover.Contents.MarkupContent!.Value;
        Assert.DoesNotContain("📦", md);
    }

    // ── BuildAssetReferenceHover ───────────────────────────────────────────────

    private static XmlTagDefinition AssetTag(ReferenceKind kind)
    {
        return new XmlTagDefinition
        {
            Tag = "Icon_Name", ValueType = XmlValueType.NameReference, ReferenceKind = kind
        };
    }

    private static IAssetFileIndex Packed(params string[] paths)
    {
        return MergedAssetFileIndex.Merge(paths, []);
    }

    private static IAssetFileIndex Loose(params string[] paths)
    {
        return MergedAssetFileIndex.Merge([], paths);
    }

    [Fact]
    public void BuildAssetReferenceHover_PackedAsset_ContainsPathAndPackedMarker()
    {
        var hover = HoverUtility.BuildAssetReferenceHover(
            AssetTag(ReferenceKind.TextureFile), "foo.tga",
            Packed("data/art/textures/foo.tga"), 5, 10, 7);
        var md = hover!.Contents.MarkupContent!.Value;
        Assert.Contains("data/art/textures/foo.tga", md);
        Assert.Contains("📦", md);
    }

    [Fact]
    public void BuildAssetReferenceHover_LooseAsset_ContainsPathNoPackedMarker()
    {
        var hover = HoverUtility.BuildAssetReferenceHover(
            AssetTag(ReferenceKind.TextureFile), "foo.tga",
            Loose("data/art/textures/foo.tga"), 0, 0, 7);
        var md = hover!.Contents.MarkupContent!.Value;
        Assert.Contains("data/art/textures/foo.tga", md);
        Assert.DoesNotContain("📦", md);
    }

    [Fact]
    public void BuildAssetReferenceHover_NotInIndex_ReturnsNull()
    {
        var result = HoverUtility.BuildAssetReferenceHover(
            AssetTag(ReferenceKind.TextureFile), "missing.tga",
            MergedAssetFileIndex.Merge([], []), 0, 0, 11);
        Assert.Null(result);
    }

    [Fact]
    public void BuildAssetReferenceHover_RangeIsAtCursorPosition()
    {
        var hover = HoverUtility.BuildAssetReferenceHover(
            AssetTag(ReferenceKind.TextureFile), "foo.tga",
            Loose("data/art/textures/foo.tga"), 3, 12, 7);
        Assert.Equal(3, hover!.Range!.Start.Line);
        Assert.Equal(12, hover.Range.Start.Character);
        Assert.Equal(3, hover.Range.End.Line);
        Assert.Equal(19, hover.Range.End.Character); // 12 + 7
    }

    [Fact]
    public void BuildAssetReferenceHover_TextureKind_ContainsTextureLabel()
    {
        var hover = HoverUtility.BuildAssetReferenceHover(
            AssetTag(ReferenceKind.TextureFile), "foo.tga",
            Loose("data/art/textures/foo.tga"), 0, 0, 7);
        Assert.Contains("Texture", hover!.Contents.MarkupContent!.Value);
    }

    [Fact]
    public void BuildAssetReferenceHover_ModelKind_ContainsModelLabel()
    {
        var hover = HoverUtility.BuildAssetReferenceHover(
            AssetTag(ReferenceKind.ModelFile), "unit.alo",
            Loose("data/art/models/unit.alo"), 0, 0, 8);
        Assert.Contains("Model", hover!.Contents.MarkupContent!.Value);
    }

    [Fact]
    public void BuildAssetReferenceHover_MultipleMatches_AllPathsShown()
    {
        var index = Loose("data/art/textures/foo.tga", "data/art/textures/patch/foo.tga");
        var hover = HoverUtility.BuildAssetReferenceHover(
            AssetTag(ReferenceKind.TextureFile), "foo.tga", index, 0, 0, 7);
        var md = hover!.Contents.MarkupContent!.Value;
        Assert.Contains("data/art/textures/foo.tga", md);
        Assert.Contains("data/art/textures/patch/foo.tga", md);
    }
}