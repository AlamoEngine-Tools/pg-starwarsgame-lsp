// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Pins StoryRewardType parameters to what each engine handler reads, measured against the
///     reward dispatch in the 2018 build. Loads the real schema/eaw/ YAML file.
/// </summary>
public sealed class EawSchemaStoryRewardEngineTest
{
    private const string RewardFile = "enums/StoryRewardType.yaml";

    private static readonly RawEnumDefinition Rewards =
        YamlSchemaParser.ParseEnumFile(EawSchemaRepo.Read(RewardFile));

    // Read with atof, not atoi.
    [Theory]
    [InlineData("CREDITS", 0)]
    [InlineData("BOMBARD_DELAY", 0)]
    [InlineData("SCREEN_TEXT", 1)]
    public void Param_IsAFloat(string reward, int position)
    {
        Assert.Equal(XmlValueType.Float, Param(reward, position).ValueType);
    }

    // The handler defaults these to on when the param is empty.
    [Theory]
    [InlineData("ENABLE_COMBAT_CINEMATIC", 0)]
    [InlineData("ENABLE_FLEET_COMBINE", 0)]
    [InlineData("ENABLE_OVERWHELMING_ODDS", 0)]
    [InlineData("ENABLE_SABOTAGE", 0)]
    [InlineData("ENABLE_INVASION", 1)]
    [InlineData("ENABLE_GALACTIC_CORRUPTION_HOLOGRAM", 1)]
    [InlineData("RESTRICT_ALL_ABILITIES", 1)]
    [InlineData("RESTRICT_AUTORESOLVE", 1)]
    [InlineData("RESTRICT_BLACK_MARKET", 1)]
    [InlineData("RESTRICT_CORRUPTION", 1)]
    [InlineData("RESTRICT_SABOTAGE", 1)]
    [InlineData("BUILDABLE_UNIT", 0)]
    [InlineData("SET_TACTICAL_MAP", 2)]
    public void Param_IsOptional(string reward, int position)
    {
        Assert.True(Param(reward, position).Optional, $"{reward} param {position} should be optional.");
    }

    // The handler skips the whole reward when these are empty.
    [Theory]
    [InlineData("SET_SPAWN", 0)]
    [InlineData("SET_SPAWN", 1)]
    [InlineData("DUAL_FLASH", 0)]
    [InlineData("DUAL_FLASH", 1)]
    [InlineData("HIDE_CURSOR_ON_CLICK", 0)]
    public void Param_IsRequired(string reward, int position)
    {
        Assert.False(Param(reward, position).Optional, $"{reward} param {position} should be required.");
    }

    [Theory]
    [InlineData("LOAD_CAMPAIGN", 0, "Campaign")]
    [InlineData("FINISHED_TUTORIAL", 0, "Campaign")]
    [InlineData("GIVE_BLACK_MARKET", 0, "BlackMarketItem")]
    [InlineData("SFX", 0, "SFXEvent")]
    [InlineData("POSITION_CAMERA", 0, "GameObjectType")]
    [InlineData("SCROLL_CAMERA", 0, "GameObjectType")]
    [InlineData("FLASH_PLANET", 0, "GameObjectType")]
    public void Param_ReferencesWhatTheHandlerLooksUp(string reward, int position, string referenceType)
    {
        Assert.Equal(referenceType, Param(reward, position).ReferenceType);
    }

    // The handler converts it with the unit ability type table (STEALTH, TURBO, ...), not a
    // SpecialAbility object.
    [Fact]
    public void FlashSpecialAbility_IsNotASpecialAbilityObject()
    {
        Assert.NotEqual("SpecialAbility", Param("FLASH_SPECIAL_ABILITY", 0).ReferenceType);
    }

    // Params 0..2 are all handed on to the tutorial event toggle.
    [Theory]
    [InlineData("DISABLE_EVENT")]
    [InlineData("ENABLE_EVENT")]
    public void TutorialEventToggle_HasThreeParams(string reward)
    {
        Assert.True(Param(reward, 2).Optional);
    }

    [Fact]
    public void RewardFile_IsAscii()
    {
        var text = EawSchemaRepo.Read(RewardFile);
        var offending = text.Select((c, i) => (c, i)).Where(t => t.c > 127 && t.c != '﻿').ToList();

        Assert.True(offending.Count == 0,
            $"Non-ASCII characters at offsets {string.Join(", ", offending.Select(t => t.i).Take(5))}.");
    }

    private static RawParamDefinition Param(string reward, int position)
    {
        var value = Rewards.Values.FirstOrDefault(v => v.Name == reward);
        Assert.NotNull(value);
        var param = value.Params?.FirstOrDefault(p => p.Position == position);
        Assert.NotNull(param);
        return param;
    }
}