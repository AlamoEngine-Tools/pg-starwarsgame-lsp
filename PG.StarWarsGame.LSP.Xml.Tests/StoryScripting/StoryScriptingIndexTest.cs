// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Xml.StoryScripting;

namespace PG.StarWarsGame.LSP.Xml.Tests.StoryScripting;

public sealed class StoryScriptingIndexTest
{
    // -----------------------------------------------------------------------
    // AllEvents / AllRewards coverage
    // -----------------------------------------------------------------------

    [Fact]
    public void AllEvents_contains_37_event_types()
    {
        Assert.Equal(37, StoryScriptingIndex.AllEvents.Count);
    }

    [Fact]
    public void AllRewards_contains_at_least_70_reward_types()
    {
        Assert.True(StoryScriptingIndex.AllRewards.Count >= 70);
    }

    // -----------------------------------------------------------------------
    // GetEvent — happy paths
    // -----------------------------------------------------------------------

    [Fact]
    public void GetEvent_STORY_ACCUMULATE_has_one_required_PositiveInteger_param()
    {
        var def = StoryScriptingIndex.GetEvent("STORY_ACCUMULATE");

        Assert.NotNull(def);
        Assert.Single(def.Params);
        var p = def.Params[0];
        Assert.Equal(1, p.Position);
        Assert.Equal(StoryParamKind.PositiveInteger, p.Kind);
        Assert.True(p.Required);
    }

    [Fact]
    public void GetEvent_STORY_FLAG_has_three_params_with_correct_kinds()
    {
        var def = StoryScriptingIndex.GetEvent("STORY_FLAG");

        Assert.NotNull(def);
        Assert.Equal(3, def.Params.Count);

        Assert.Equal(StoryParamKind.FlagNameRef, def.Params[0].Kind);
        Assert.True(def.Params[0].Required);

        Assert.Equal(StoryParamKind.Integer, def.Params[1].Kind);
        Assert.True(def.Params[1].Required);

        Assert.Equal(StoryParamKind.Enum, def.Params[2].Kind);
        Assert.True(def.Params[2].Required);
        Assert.Equal("StoryFlagCompareMethod", def.Params[2].EnumName);
    }

    [Fact]
    public void GetEvent_STORY_MOVIE_DONE_has_no_params()
    {
        var def = StoryScriptingIndex.GetEvent("STORY_MOVIE_DONE");

        Assert.NotNull(def);
        Assert.Empty(def.Params);
    }

    [Fact]
    public void GetEvent_STORY_TRIGGER_has_no_params()
    {
        var def = StoryScriptingIndex.GetEvent("STORY_TRIGGER");

        Assert.NotNull(def);
        Assert.Empty(def.Params);
    }

    [Fact]
    public void GetEvent_STORY_CONQUER_has_correct_structure()
    {
        var def = StoryScriptingIndex.GetEvent("STORY_CONQUER");

        Assert.NotNull(def);
        Assert.Equal(3, def.Params.Count);
        Assert.Equal(StoryParamKind.PlanetRef, def.Params[0].Kind);
        Assert.True(def.Params[0].Required);
        Assert.Equal(StoryParamKind.FactionRef, def.Params[2].Kind);
        Assert.False(def.Params[2].Required);
    }

    [Fact]
    public void GetEvent_STORY_GENERIC_has_EnumList_param_with_StoryGenericTriggerType()
    {
        var def = StoryScriptingIndex.GetEvent("STORY_GENERIC");

        Assert.NotNull(def);
        Assert.Single(def.Params);
        Assert.Equal(StoryParamKind.EnumList, def.Params[0].Kind);
        Assert.Equal("StoryGenericTriggerType", def.Params[0].EnumName);
    }

    [Fact]
    public void GetEvent_STORY_ENTER_SupportsFilter_is_true()
    {
        var def = StoryScriptingIndex.GetEvent("STORY_ENTER");

        Assert.NotNull(def);
        Assert.True(def.SupportsFilter);
    }

    [Fact]
    public void GetEvent_STORY_ACCUMULATE_SupportsFilter_is_false()
    {
        var def = StoryScriptingIndex.GetEvent("STORY_ACCUMULATE");

        Assert.NotNull(def);
        Assert.False(def.SupportsFilter);
    }

    // -----------------------------------------------------------------------
    // GetEvent — null / unknown → null
    // -----------------------------------------------------------------------

    [Fact]
    public void GetEvent_unknown_type_returns_null()
    {
        Assert.Null(StoryScriptingIndex.GetEvent("STORY_DOES_NOT_EXIST"));
    }

    [Fact]
    public void GetEvent_null_returns_null()
    {
        Assert.Null(StoryScriptingIndex.GetEvent(null!));
    }

    // -----------------------------------------------------------------------
    // GetEvent — case-insensitive lookup
    // -----------------------------------------------------------------------

    [Fact]
    public void GetEvent_lookup_is_case_insensitive()
    {
        Assert.NotNull(StoryScriptingIndex.GetEvent("story_flag"));
        Assert.NotNull(StoryScriptingIndex.GetEvent("Story_Flag"));
        Assert.NotNull(StoryScriptingIndex.GetEvent("STORY_FLAG"));
    }

    // -----------------------------------------------------------------------
    // GetReward — happy paths
    // -----------------------------------------------------------------------

    [Fact]
    public void GetReward_FLASH_UNIT_has_Enum_then_GameObjectTypeRef_params()
    {
        var def = StoryScriptingIndex.GetReward("FLASH_UNIT");

        Assert.NotNull(def);
        Assert.Equal(2, def.Params.Count);

        var p1 = def.Params[0];
        Assert.Equal(1, p1.Position);
        Assert.Equal(StoryParamKind.Enum, p1.Kind);
        Assert.Equal("StoryCommandBarRegion", p1.EnumName);
        Assert.True(p1.Required);

        var p2 = def.Params[1];
        Assert.Equal(2, p2.Position);
        Assert.Equal(StoryParamKind.GameObjectTypeRef, p2.Kind);
        Assert.True(p2.Required);
    }

    [Fact]
    public void GetReward_CREDITS_has_one_PositiveInteger_param()
    {
        var def = StoryScriptingIndex.GetReward("CREDITS");

        Assert.NotNull(def);
        Assert.Single(def.Params);
        Assert.Equal(StoryParamKind.PositiveInteger, def.Params[0].Kind);
        Assert.True(def.Params[0].Required);
    }

    [Fact]
    public void GetReward_LINK_TACTICAL_has_13_params()
    {
        var def = StoryScriptingIndex.GetReward("LINK_TACTICAL");

        Assert.NotNull(def);
        Assert.Equal(13, def.Params.Count);
    }

    [Fact]
    public void GetReward_ZOOM_IN_has_no_params()
    {
        var def = StoryScriptingIndex.GetReward("ZOOM_IN");

        Assert.NotNull(def);
        Assert.Empty(def.Params);
    }

    [Fact]
    public void GetReward_DISABLE_EVENT_Param1_is_StoryTutorialEventType()
    {
        var def = StoryScriptingIndex.GetReward("DISABLE_EVENT");

        Assert.NotNull(def);
        Assert.Equal(StoryParamKind.Enum, def.Params[0].Kind);
        Assert.Equal("StoryTutorialEventType", def.Params[0].EnumName);
    }

    [Fact]
    public void GetReward_FLASH_PLANET_GUI_Param2_is_StoryPlanetGuiFlashElement()
    {
        var def = StoryScriptingIndex.GetReward("FLASH_PLANET_GUI");

        Assert.NotNull(def);
        Assert.Equal(StoryParamKind.Enum, def.Params[1].Kind);
        Assert.Equal("StoryPlanetGuiFlashElement", def.Params[1].EnumName);
    }

    [Fact]
    public void GetReward_SPEECH_has_SpeechEventRef_param()
    {
        var def = StoryScriptingIndex.GetReward("SPEECH");

        Assert.NotNull(def);
        Assert.Single(def.Params);
        Assert.Equal(StoryParamKind.SpeechEventRef, def.Params[0].Kind);
    }

    // -----------------------------------------------------------------------
    // GetReward — null / unknown → null
    // -----------------------------------------------------------------------

    [Fact]
    public void GetReward_unknown_type_returns_null()
    {
        Assert.Null(StoryScriptingIndex.GetReward("NOT_A_REWARD"));
    }

    [Fact]
    public void GetReward_null_returns_null()
    {
        Assert.Null(StoryScriptingIndex.GetReward(null!));
    }

    // -----------------------------------------------------------------------
    // GetReward — case-insensitive lookup
    // -----------------------------------------------------------------------

    [Fact]
    public void GetReward_lookup_is_case_insensitive()
    {
        Assert.NotNull(StoryScriptingIndex.GetReward("flash_unit"));
        Assert.NotNull(StoryScriptingIndex.GetReward("Flash_Unit"));
        Assert.NotNull(StoryScriptingIndex.GetReward("FLASH_UNIT"));
    }

    // -----------------------------------------------------------------------
    // Param positions are 1-based and sequential
    // -----------------------------------------------------------------------

    [Fact]
    public void Params_positions_are_1based_and_sequential()
    {
        foreach (var ev in StoryScriptingIndex.AllEvents)
        {
            for (var i = 0; i < ev.Params.Count; i++)
                Assert.Equal(i + 1, ev.Params[i].Position);
        }

        foreach (var rw in StoryScriptingIndex.AllRewards)
        {
            for (var i = 0; i < rw.Params.Count; i++)
                Assert.Equal(i + 1, rw.Params[i].Position);
        }
    }

    // -----------------------------------------------------------------------
    // Enum params always carry an EnumName
    // -----------------------------------------------------------------------

    [Fact]
    public void Every_Enum_or_EnumList_param_has_a_non_null_EnumName()
    {
        var allParams = StoryScriptingIndex.AllEvents.SelectMany(e => e.Params)
            .Concat(StoryScriptingIndex.AllRewards.SelectMany(r => r.Params));

        foreach (var p in allParams)
        {
            if (p.Kind is StoryParamKind.Enum or StoryParamKind.EnumList)
                Assert.NotNull(p.EnumName);
        }
    }
}
