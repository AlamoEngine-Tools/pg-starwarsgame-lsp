// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Assets;

/// <summary>
///     Which animation files belong to a model - the rule the preview built, shared so a diagnostic asks the
///     same question the same way.
/// </summary>
public sealed class ModelAnimationClipsTest
{
    private static GameIndex Index(string[] models, params string[] files)
    {
        var bones = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models) bones[model] = ["Root"];

        return GameIndex.Empty with
        {
            ModelBones = bones.ToImmutable(),
            AssetFiles = new MergedAssetFileIndex(files)
        };
    }

    [Fact]
    public void For_lists_the_model_s_clips_by_file_name_in_order()
    {
        var index = Index(["ni_mandalorian.alo"],
            "data/art/models/ni_mandalorian_move_00.ala",
            "data/art/models/ni_mandalorian_die_00.ala",
            "data/art/models/ni_mandalorian.alo");

        Assert.Equal(["ni_mandalorian_die_00.ala", "ni_mandalorian_move_00.ala"],
            ModelAnimationClips.For(index, "NI_Mandalorian.alo"));
    }

    // The separator matters: Ei_bobafettish_wave is not Boba Fett's.
    [Fact]
    public void For_needs_the_separator_after_the_model_name()
    {
        var index = Index(["ei_bobafett.alo"],
            "data/art/models/ei_bobafett_idle_00.ala",
            "data/art/models/ei_bobafettish_wave_00.ala");

        Assert.Equal(["ei_bobafett_idle_00.ala"], ModelAnimationClips.For(index, "EI_BobaFett.alo"));
    }

    // The longest model name prefixing a clip owns it: a hull must not collect its death clone's clips.
    [Fact]
    public void For_leaves_a_longer_model_s_clips_to_that_model()
    {
        var index = Index(["rv_gargantuan.alo", "rv_gargantuan_dc.alo"],
            "data/art/models/rv_gargantuan_idle_00.ala",
            "data/art/models/rv_gargantuan_dc_die_00.ala");

        Assert.Equal(["rv_gargantuan_idle_00.ala"], ModelAnimationClips.For(index, "RV_Gargantuan.alo"));
        Assert.Equal(["rv_gargantuan_dc_die_00.ala"], ModelAnimationClips.For(index, "RV_Gargantuan_DC.alo"));
    }

    // An XML value can carry a path with either slash, or no extension at all.
    [Theory]
    [InlineData("Data\\Art\\Models\\NI_Mandalorian.alo")]
    [InlineData("Data/Art/Models/NI_Mandalorian.ALO")]
    [InlineData("NI_Mandalorian")]
    public void For_takes_the_model_name_from_any_reference_form(string reference)
    {
        var index = Index(["ni_mandalorian.alo"], "data/art/models/ni_mandalorian_die_00.ala");

        Assert.Equal(["ni_mandalorian_die_00.ala"], ModelAnimationClips.For(index, reference));
    }

    [Fact]
    public void For_is_empty_for_a_blank_reference()
    {
        Assert.Empty(ModelAnimationClips.For(Index(["a.alo"], "data/art/models/a_die_00.ala"), "  "));
    }

    /// <summary>
    ///     <c>ModelAnimsListClass::Get_Total_Animations_Of_Type</c>, as the file names spell it: every take of
    ///     <c>&lt;model&gt;_&lt;type&gt;_&lt;nn&gt;.ala</c>.
    /// </summary>
    [Fact]
    public void CountOfType_counts_every_take_of_the_type()
    {
        string[] clips =
            ["ni_gungan_die_00.ala", "ni_gungan_die_01.ala", "ni_gungan_fw_die_00.ala", "ni_gungan_move_00.ala"];

        Assert.Equal(2, ModelAnimationClips.CountOfType(clips, "NI_Gungan.alo", "DIE"));
        Assert.Equal(1, ModelAnimationClips.CountOfType(clips, "NI_Gungan.alo", "fw_die"));
        Assert.Equal(0, ModelAnimationClips.CountOfType(clips, "NI_Gungan.alo", "Crushed"));
    }

    // DEPLOYED_DIE is its own type; a DIE count must not pick it up, nor the other way round.
    [Fact]
    public void CountOfType_matches_the_whole_type_name()
    {
        string[] clips = ["ev_at-at_deployed_die_00.ala", "ev_at-at_die_00.ala", "ev_at-at_die_01.ala"];

        Assert.Equal(2, ModelAnimationClips.CountOfType(clips, "EV_AT-AT.alo", "DIE"));
        Assert.Equal(1, ModelAnimationClips.CountOfType(clips, "EV_AT-AT.alo", "DEPLOYED_DIE"));
    }
}