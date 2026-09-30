// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>A <c>Story_Event("Id")</c> the phase reaches, after the sleeps on the way to it.</summary>
public sealed record LuaStoryEmission(string Id, double DelaySeconds);

/// <summary>A <c>Spawn_Unit</c> / <c>SpawnList</c> the phase reaches; the planet when it could be read.</summary>
public sealed record LuaStorySpawn(string UnitType, string? Planet);

/// <summary>
///     What one phase of a state function does, read statically: the notifications it emits with
///     their sleep delays, the states it moves to, the units it spawns, and the threads it starts.
/// </summary>
public sealed record LuaStoryPhase(
    IReadOnlyList<LuaStoryEmission> Emissions,
    IReadOnlyList<string> Transitions,
    IReadOnlyList<LuaStorySpawn> Spawns,
    IReadOnlyList<string> Threads)
{
    public static readonly LuaStoryPhase Empty = new([], [], [], []);
}

/// <summary>One <c>StoryModeEvents</c> entry: the XML event name it answers to and its three phases.</summary>
public sealed record LuaStoryState(
    string Name,
    string FunctionName,
    LuaStoryPhase OnEnter,
    LuaStoryPhase OnUpdate,
    LuaStoryPhase OnExit);

/// <summary>
///     A story script as the PGStateMachine runs it: the states its <c>StoryModeEvents</c> table
///     declares, each with the effects its phases reach. The machine itself (OnExit, state change,
///     OnEnter, OnUpdate per service pass) is measured from the shipped library; only the effects
///     are inferred by static extraction.
/// </summary>
public sealed record LuaStoryMachine(string ScriptUri, string ScriptName, IReadOnlyList<LuaStoryState> States);
