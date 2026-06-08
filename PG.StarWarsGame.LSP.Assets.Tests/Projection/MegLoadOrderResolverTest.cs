// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Projection;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

public sealed class MegLoadOrderResolverTest
{
    private const string Root = "C:/game";

    // ── Base MEG ordering ─────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoPatches_ReturnsSortedBaseMegs()
    {
        string[] paths = ["C:/game/z_data.meg", "C:/game/a_data.meg", "C:/game/b_data.meg"];

        var result = MegLoadOrderResolver.Resolve(paths, Root);

        Assert.Equal(["C:/game/a_data.meg", "C:/game/b_data.meg", "C:/game/z_data.meg"], result);
    }

    [Fact]
    public void Resolve_AllThreePatches_AppendedAfterBaseMegsInFixedOrder()
    {
        string[] paths =
        [
            "C:/game/patch2.meg",
            "C:/game/patch.meg",
            "C:/game/64patch.meg",
            "C:/game/base.meg"
        ];

        var result = MegLoadOrderResolver.Resolve(paths, Root);

        Assert.Equal(
            ["C:/game/base.meg", "C:/game/patch.meg", "C:/game/patch2.meg", "C:/game/64patch.meg"],
            result);
    }

    [Fact]
    public void Resolve_OnlySomePatchesPresent_OnlyPresentPatchesAppended()
    {
        string[] paths = ["C:/game/base.meg", "C:/game/64patch.meg", "C:/game/patch2.meg"];

        var result = MegLoadOrderResolver.Resolve(paths, Root);

        Assert.Equal(["C:/game/base.meg", "C:/game/patch2.meg", "C:/game/64patch.meg"], result);
    }

    [Fact]
    public void Resolve_PatchNamesAreCaseInsensitive()
    {
        string[] paths = ["C:/game/base.meg", "C:/game/PATCH.MEG", "C:/game/Patch2.Meg"];

        var result = MegLoadOrderResolver.Resolve(paths, Root);

        Assert.Equal(["C:/game/base.meg", "C:/game/PATCH.MEG", "C:/game/Patch2.Meg"], result);
    }

    // ── SFX language selection ────────────────────────────────────────────────

    [Fact]
    public void Resolve_SfxWithSingleLanguageVariant_Included()
    {
        string[] paths = ["C:/game/Data/Audio/SFX/voice_english.meg"];

        var result = MegLoadOrderResolver.Resolve(paths, Root);

        Assert.Single(result);
        Assert.Contains("C:/game/Data/Audio/SFX/voice_english.meg", result);
    }

    [Fact]
    public void Resolve_SfxWithMultipleLanguages_OnlyEnglishIncluded()
    {
        string[] paths =
        [
            "C:/game/Data/Audio/SFX/voice_english.meg",
            "C:/game/Data/Audio/SFX/voice_german.meg",
            "C:/game/Data/Audio/SFX/voice_french.meg"
        ];

        var result = MegLoadOrderResolver.Resolve(paths, Root);

        Assert.Single(result);
        Assert.Contains("C:/game/Data/Audio/SFX/voice_english.meg", result);
    }

    [Fact]
    public void Resolve_SfxNonLocalizedAlwaysIncluded_EvenWhenMultipleLanguagesPresent()
    {
        string[] paths =
        [
            "C:/game/Data/Audio/SFX/sfx_non_localized.meg",
            "C:/game/Data/Audio/SFX/voice_english.meg",
            "C:/game/Data/Audio/SFX/voice_german.meg"
        ];

        var result = MegLoadOrderResolver.Resolve(paths, Root);

        Assert.Equal(2, result.Count);
        Assert.Contains("C:/game/Data/Audio/SFX/sfx_non_localized.meg", result);
        Assert.Contains("C:/game/Data/Audio/SFX/voice_english.meg", result);
        Assert.DoesNotContain("C:/game/Data/Audio/SFX/voice_german.meg", result);
    }

    [Fact]
    public void Resolve_SfxPathDetectionIsCaseInsensitive()
    {
        // Backslash Windows-style path under DATA\AUDIO\SFX should be treated as SFX.
        string[] paths =
        [
            @"C:\game\DATA\AUDIO\SFX\voice_english.meg",
            @"C:\game\DATA\AUDIO\SFX\voice_german.meg"
        ];

        var result = MegLoadOrderResolver.Resolve(paths, Root);

        Assert.Single(result);
        Assert.Contains(@"C:\game\DATA\AUDIO\SFX\voice_english.meg", result);
    }

    // ── Combined ordering ─────────────────────────────────────────────────────

    [Fact]
    public void Resolve_MixedBaseAndSfxAndPatches_CorrectOrder()
    {
        string[] paths =
        [
            "C:/game/patch.meg",
            "C:/game/z_art.meg",
            "C:/game/a_art.meg",
            "C:/game/Data/Audio/SFX/sfx_non_localized.meg",
            "C:/game/Data/Audio/SFX/voice_english.meg",
            "C:/game/Data/Audio/SFX/voice_german.meg",
            "C:/game/patch2.meg"
        ];

        var result = MegLoadOrderResolver.Resolve(paths, Root);

        // Base MEGs (including SFX-filtered) sorted, then patches in order.
        // Non-patch MEGs sorted: a_art, Data/..sfx_non_localized, Data/..voice_english, z_art
        Assert.Equal(6, result.Count);
        Assert.Equal("C:/game/patch.meg", result[^2]);
        Assert.Equal("C:/game/patch2.meg", result[^1]);
        Assert.DoesNotContain("C:/game/Data/Audio/SFX/voice_german.meg", result);
    }
}