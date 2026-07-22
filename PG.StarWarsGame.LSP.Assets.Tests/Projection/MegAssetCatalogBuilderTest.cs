// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Assets.Projection;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

public sealed class MegAssetCatalogBuilderTest
{
    // ── NormalizeMegPath ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"DATA\ART\TEXTURES\FOO.TGA", "data/art/textures/foo.tga")]
    [InlineData("data/art/textures/foo.tga", "data/art/textures/foo.tga")]
    [InlineData(@"\DATA\ART\FOO.TGA", "data/art/foo.tga")]
    [InlineData("/data/art/foo.tga", "data/art/foo.tga")]
    [InlineData(@"DATA\AUDIO\MUSIC.MP3", "data/audio/music.mp3")]
    public void NormalizeMegPath_ConvertsToLowercaseForwardSlashNoLeadingSlash(string input, string expected)
    {
        Assert.Equal(expected, MegAssetCatalogBuilder.NormalizeMegPath(input));
    }

    // ── Extension filtering ───────────────────────────────────────────────────

    [Theory]
    [InlineData(".tga", true)]
    [InlineData(".TGA", true)]
    [InlineData(".dds", true)]
    [InlineData(".alo", true)]
    [InlineData(".wav", true)]
    [InlineData(".mp3", true)]
    [InlineData(".ted", true)]
    [InlineData(".xml", false)]
    [InlineData(".lua", false)]
    [InlineData(".exe", false)]
    public void IsAssetExtension_ReturnsCorrectResult(string ext, bool expected)
    {
        Assert.Equal(expected, MegAssetCatalogBuilder.IsAssetExtension(ext));
    }

    // ── Build - asset file collection ────────────────────────────────────────

    [Fact]
    public void Build_EmptyMegsAndNoLooseFiles_ReturnsEmpty()
    {
        var fs = new MockFileSystem();
        var (assets, bones) = MegAssetCatalogBuilder.Build(
            [], fs, "C:/game", _ => null, _ => [], null, NullLogger.Instance);

        Assert.Empty(assets);
        Assert.Empty(bones);
    }

    [Fact]
    public void Build_MegEntryWithAssetExtension_IncludedInAssetFiles()
    {
        var megs = OneMeg("test.meg", [@"DATA\ART\TEXTURES\UNIT.TGA", @"DATA\ART\MODELS\UNIT.ALO"]);
        var fs = new MockFileSystem();

        var (assets, _) = Build(megs, fs);

        Assert.Contains("data/art/textures/unit.tga", assets);
        Assert.Contains("data/art/models/unit.alo", assets);
    }

    [Fact]
    public void Build_MegEntryWithNonAssetExtension_Excluded()
    {
        var megs = OneMeg("test.meg", [@"DATA\XML\UNITS.XML", @"DATA\ART\TEXTURES\UNIT.TGA"]);
        var fs = new MockFileSystem();

        var (assets, _) = Build(megs, fs);

        Assert.DoesNotContain("data/xml/units.xml", assets);
        Assert.Contains("data/art/textures/unit.tga", assets);
    }

    [Fact]
    public void Build_LooseFilesAdded_MergedWithMegAssets()
    {
        var megs = OneMeg("test.meg", [@"DATA\ART\TEXTURES\MEG_TEX.TGA"]);
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["C:/game/data/art/textures/loose_tex.tga"] = new("")
        });

        var (assets, _) = Build(megs, fs);

        Assert.Contains("data/art/textures/meg_tex.tga", assets);
        Assert.Contains("data/art/textures/loose_tex.tga", assets);
    }

    [Fact]
    public void Build_SameNormalizedPathInTwoMegs_LastWins_BothPathsInAssetSet()
    {
        // Collision: same path in two different MEG archives.
        // Last-seen entry wins (engine VFS semantics). The path is still in the set.
        var megs = new[]
        {
            ("first.meg", new[] { @"DATA\ART\TEXTURES\SHARED.TGA" }),
            ("second.meg", (IEnumerable<string>)new[] { @"DATA\ART\TEXTURES\SHARED.TGA" })
        };
        var fs = new MockFileSystem();

        var (assets, _) = Build(megs, fs);

        Assert.Contains("data/art/textures/shared.tga", assets);
        Assert.Single(assets, a => a == "data/art/textures/shared.tga");
    }

    // ── Build - model bone extraction ─────────────────────────────────────────

    [Fact]
    public void Build_AloEntryWithBones_ExtractsBonesKeyedByFilename()
    {
        var megs = OneMeg("test.meg", [@"DATA\ART\MODELS\UNIT_AT_AT.ALO"]);
        var fs = new MockFileSystem();
        var bones = new[] { "Bone_Root", "Bone_Head" };

        var (_, modelBones) = MegAssetCatalogBuilder.Build(
            megs, fs, "C:/game",
            path =>
            {
                if (path == "data/art/models/unit_at_at.alo")
                    return new MemoryStream();
                return null;
            },
            _ => bones,
            null,
            NullLogger.Instance);

        // Bones are keyed by bare filename: XML references models by name, never by path, and the
        // engine resolves them the same way. See ModelBoneKey.
        Assert.True(modelBones.ContainsKey("unit_at_at.alo"));
        Assert.Equal<IEnumerable<string>>(bones, modelBones["unit_at_at.alo"]);
    }

    [Fact]
    public void Build_AloEntryOpenReturnsNull_SkippedInBones()
    {
        var megs = OneMeg("test.meg", [@"DATA\ART\MODELS\UNIT.ALO"]);
        var fs = new MockFileSystem();

        var (_, modelBones) = Build(megs, fs, openEntry: _ => null);

        Assert.Empty(modelBones);
    }

    [Fact]
    public void Build_AloEntryBonesEmpty_NotIncludedInModelBones()
    {
        var megs = OneMeg("test.meg", [@"DATA\ART\MODELS\UNIT.ALO"]);
        var fs = new MockFileSystem();

        var (_, modelBones) = MegAssetCatalogBuilder.Build(
            megs, fs, "C:/game",
            _ => new MemoryStream(),
            _ => [],
            null,
            NullLogger.Instance);

        Assert.Empty(modelBones);
    }

    // ── SFX path conventions ──────────────────────────────────────────────────

    [Theory]
    [InlineData("SFX2D_NON_LOCALIZED.MEG", "UNIT_ATTACK.WAV", "data/audio/sfx/unit_attack.wav")]
    [InlineData("SFX3D_NON_LOCALIZED.MEG", "AMBIENT_WIND.WAV", "data/audio/sfx/ambient_wind.wav")]
    [InlineData("SFX2D_ENGLISH.MEG", "UNIT_MOVE_ENG.WAV", "data/audio/sfx/unit_move.wav")]
    [InlineData("SFX2D_ENGLISH.MEG", "NO_SUFFIX.WAV", "data/audio/sfx/no_suffix.wav")]
    public void ApplySfxConventions_FlatSfxEntry_PrefixedAndEngStripped(string megName, string rawPath, string expected)
    {
        Assert.Equal(expected, MegAssetCatalogBuilder.ApplySfxConventions(
            MegAssetCatalogBuilder.NormalizeMegPath(rawPath), megName));
    }

    [Theory]
    [InlineData(@"DATA\AUDIO\SFX\UNIT_ATTACK.WAV")]
    [InlineData("data/audio/sfx/unit_attack.wav")]
    public void ApplySfxConventions_FullPathSfxEntry_NotPrefixedAgain(string rawPath)
    {
        const string megName = "SFX2D_NON_LOCALIZED.MEG";
        var normalized = MegAssetCatalogBuilder.NormalizeMegPath(rawPath);
        Assert.Equal(normalized, MegAssetCatalogBuilder.ApplySfxConventions(normalized, megName));
    }

    [Fact]
    public void ApplySfxConventions_FlatPathFromNonSfxMeg_NotPrefixed()
    {
        const string rawPath = "UNIT.TGA";
        var normalized = MegAssetCatalogBuilder.NormalizeMegPath(rawPath);
        Assert.Equal("unit.tga", MegAssetCatalogBuilder.ApplySfxConventions(normalized, "FoC_Art.meg"));
    }

    [Theory]
    [InlineData("SFX2D_ENGLISH.MEG", @"DATA\AUDIO\SFX\SOUND_ENG.WAV", "data/audio/sfx/sound.wav")]
    [InlineData("FoC_Art.meg", @"DATA\ART\UNIT_ENG.ALO", "data/art/unit_eng.alo")]
    public void ApplySfxConventions_EngSuffix_StrippedOnlyForAudio(string megName, string rawPath, string expected)
    {
        Assert.Equal(expected, MegAssetCatalogBuilder.ApplySfxConventions(
            MegAssetCatalogBuilder.NormalizeMegPath(rawPath), megName));
    }

    [Fact]
    public void Build_FlatSfxEntry_StoredWithSfxPrefix()
    {
        var megs = OneMeg("SFX2D_NON_LOCALIZED.MEG", ["UNIT_ATTACK.WAV"]);
        var (assets, _) = Build(megs, new MockFileSystem());
        Assert.Contains("data/audio/sfx/unit_attack.wav", assets);
        Assert.DoesNotContain("unit_attack.wav", assets);
    }

    [Fact]
    public void Build_EngSuffixAudio_StoredWithoutSuffix()
    {
        var megs = OneMeg("SFX2D_ENGLISH.MEG", ["UNIT_MOVE_ENG.WAV"]);
        var (assets, _) = Build(megs, new MockFileSystem());
        Assert.Contains("data/audio/sfx/unit_move.wav", assets);
        Assert.DoesNotContain("data/audio/sfx/unit_move_eng.wav", assets);
    }

    // ── MTD icon extraction ───────────────────────────────────────────────────

    [Fact]
    public void Build_MtdMegEntry_IconNamesAddedToAssetSet()
    {
        var mtdStream = new MemoryStream();
        var megs = OneMeg("FoC_Art.meg", [@"DATA\ART\TEXTURES\MT_COMMANDBAR.MTD"]);

        Func<string, Stream?> openEntry = path =>
            path == "data/art/textures/mt_commandbar.mtd" ? mtdStream : null;

        IEnumerable<string> ExtractIcons(Stream _)
        {
            return ["ICON_A.TGA", "ICON_B.TGA"];
        }

        var (assets, _) = Build(megs, new MockFileSystem(),
            openEntry: openEntry, extractMtdIcons: ExtractIcons);

        Assert.Contains("icon_a.tga", assets);
        Assert.Contains("icon_b.tga", assets);
        Assert.DoesNotContain("data/art/textures/mt_commandbar.mtd", assets);
    }

    [Fact]
    public void Build_MtdMegEntry_NullExtractMtdIcons_EntryIgnored()
    {
        var megs = OneMeg("FoC_Art.meg", [@"DATA\ART\TEXTURES\MT_COMMANDBAR.MTD"]);

        var (assets, _) = Build(megs, new MockFileSystem());

        Assert.DoesNotContain("data/art/textures/mt_commandbar.mtd", assets);
        Assert.Empty(assets);
    }

    [Fact]
    public void Build_MtdIconNames_NormalisedToLowercase()
    {
        var mtdStream = new MemoryStream();
        var megs = OneMeg("FoC_Art.meg", [@"DATA\ART\TEXTURES\MT_ICONS.MTD"]);

        Func<string, Stream?> openEntry = path =>
            path == "data/art/textures/mt_icons.mtd" ? mtdStream : null;

        IEnumerable<string> ExtractIcons(Stream _)
        {
            return ["BUTTON_ICON_ATTACK.TGA"];
        }

        var (assets, _) = Build(megs, new MockFileSystem(),
            openEntry: openEntry, extractMtdIcons: ExtractIcons);

        // Asset set is case-insensitive; verify the stored value is the normalised lowercase form.
        Assert.Contains("button_icon_attack.tga", assets);
        Assert.True(assets.TryGetValue("button_icon_attack.tga", out var stored) &&
                    stored == "button_icon_attack.tga");
    }

    [Fact]
    public void Build_LooseMtdFile_IconNamesAddedToAssetSet()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\game\data\art\textures\mt_commandbar.mtd"] = new([0x01, 0x02])
        });

        IEnumerable<string> ExtractIcons(Stream _)
        {
            return ["ICON_LOOSE.TGA"];
        }

        var (assets, _) = Build([], fs, extractMtdIcons: ExtractIcons);

        Assert.Contains("icon_loose.tga", assets);
    }

    [Fact]
    public void Build_LooseMtdFile_OutsideDataDir_Ignored()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\game\tools\mt_commandbar.mtd"] = new([0x01])
        });

        IEnumerable<string> ExtractIcons(Stream _)
        {
            return ["SHOULD_NOT_APPEAR.TGA"];
        }

        var (assets, _) = Build([], fs, extractMtdIcons: ExtractIcons);

        Assert.DoesNotContain("should_not_appear.tga", assets);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ImmutableHashSet<string> assetFiles, ImmutableDictionary<string, ImmutableArray<string>> modelBones)
        Build(
            IEnumerable<(string megName, IEnumerable<string> entryPaths)> megs,
            MockFileSystem fs,
            string gameRoot = "C:/game",
            Func<string, Stream?>? openEntry = null,
            Func<Stream, IReadOnlyList<string>>? extractBones = null,
            Func<Stream, IEnumerable<string>>? extractMtdIcons = null)
    {
        return MegAssetCatalogBuilder.Build(
            megs, fs, gameRoot,
            openEntry ?? (_ => null),
            extractBones ?? (_ => []),
            extractMtdIcons,
            NullLogger.Instance);
    }

    private static IEnumerable<(string, IEnumerable<string>)> OneMeg(string name, IEnumerable<string> entries)
    {
        return [(name, entries)];
    }
}