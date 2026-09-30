// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaStoryMachineExtractorTest
{
    private const string Uri = "file:///ws/data/scripts/story/story_campaign_act_i.lua";

    [Fact]
    public void Extract_SplitsAStateFunctionByPhase()
    {
        var machine = LuaStoryMachineExtractor.Extract("""
            function Definitions()
                StoryModeEvents = { Act_Begin = State_Act_Begin }
            end
            function State_Act_Begin(message)
                if message == OnEnter then
                    Story_Event("ENTERED")
                elseif message == OnUpdate then
                    Story_Event("UPDATED")
                elseif message == OnExit then
                    Story_Event("LEFT")
                end
            end
            """, Uri)!;

        var state = Assert.Single(machine.States);
        Assert.Equal("Act_Begin", state.Name);
        Assert.Equal("State_Act_Begin", state.FunctionName);
        Assert.Equal("ENTERED", Assert.Single(state.OnEnter.Emissions).Id);
        Assert.Equal("UPDATED", Assert.Single(state.OnUpdate.Emissions).Id);
        Assert.Equal("LEFT", Assert.Single(state.OnExit.Emissions).Id);
        Assert.Equal("story_campaign_act_i", machine.ScriptName);
    }

    [Fact]
    public void Extract_FollowsThreadsAndSumsSleepsOnTheWayToAnEmission()
    {
        var machine = LuaStoryMachineExtractor.Extract("""
            StoryModeEvents = { Talk = State_Talk }
            function State_Talk(message)
                if message == OnEnter then
                    Sleep(1)
                    Create_Thread("Talk_Thread")
                end
            end
            function Talk_Thread()
                Sleep(5)
                Story_Event("LINE_ONE")
                Sleep(2.5)
                Story_Event("LINE_TWO")
            end
            """, Uri)!;

        var enter = Assert.Single(machine.States).OnEnter;
        Assert.Equal(["Talk_Thread"], enter.Threads);
        Assert.Equal([("LINE_ONE", 6.0), ("LINE_TWO", 8.5)], enter.Emissions.Select(e => (e.Id, e.DelaySeconds)));
    }

    [Fact]
    public void Extract_FollowsASameFileHelperInline_AndStopsAtTheDepthLimit()
    {
        var machine = LuaStoryMachineExtractor.Extract("""
            StoryModeEvents = { A = State_A }
            function State_A(message)
                if message == OnEnter then
                    Helper()
                    Story_Event("AFTER_HELPER")
                end
            end
            function Helper()
                Sleep(3)
                Story_Event("FROM_HELPER")
                Deeper()
            end
            function Deeper()
                Deepest()
            end
            function Deepest()
                Story_Event("TOO_DEEP")
            end
            """, Uri)!;

        var enter = Assert.Single(machine.States).OnEnter;
        Assert.Equal([("FROM_HELPER", 3.0), ("AFTER_HELPER", 3.0)],
            enter.Emissions.Select(e => (e.Id, e.DelaySeconds)));
    }

    [Fact]
    public void Extract_CollectsTransitionsAndSpawns_ResolvingGlobalsOnce()
    {
        var machine = LuaStoryMachineExtractor.Extract("""
            function Definitions()
                StoryModeEvents = { A = State_A }
                unit_list = { "X_Wing", "Y_Wing" }
                hoth = FindPlanet("Hoth")
            end
            function State_A(message)
                if message == OnEnter then
                    local units = SpawnList(unit_list, hoth, Find_Player("Rebel"), false, false)
                    Spawn_Unit("Han_Solo", "Tatooine", Find_Player("Rebel"))
                    Set_Next_State("B")
                end
            end
            """, Uri)!;

        var enter = Assert.Single(machine.States).OnEnter;
        Assert.Equal(["B"], enter.Transitions);
        Assert.Equal([("X_Wing", "Hoth"), ("Y_Wing", "Hoth"), ("Han_Solo", "Tatooine")],
            enter.Spawns.Select(s => (s.UnitType, s.Planet)));
    }

    [Fact]
    public void Extract_ReturnsNull_WhenTheScriptDeclaresNoStoryModeEvents()
    {
        Assert.Null(LuaStoryMachineExtractor.Extract("function main() end", Uri));
    }
}
