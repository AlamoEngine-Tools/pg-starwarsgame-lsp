// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>Gating for the <c>aet/storySim*</c> endpoints - same pattern as <see cref="StoryEditorFeature" />.</summary>
public static class StorySimFeature
{
    public const string DisabledMessage =
        "The story simulator is disabled. Enable 'aet-eaw-edit.features.tools.storySimulator' in the editor settings.";

    public static string? Rejection(ILspConfigurationProvider config)
    {
        if (!config.Current.Features.Tools.StorySimulator) return DisabledMessage;
        if (!config.Current.Features.Story.Discovery) return StoryEditorFeature.DiscoveryMissingMessage;
        return null;
    }
}

// One flat state document per response - the graphs are small, deltas aren't worth the
// bookkeeping - except for the trace, which grows with every tick: a response carries the steps
// from the caller's SinceSeq on, and TotalSteps tells the caller where to continue. No
// dictionaries in the DTOs: OmniSharp's serializer camelCases dictionary string keys, which
// would corrupt flag names and node ids.

public sealed record StorySimStateDto(
    bool Running,
    int Tick,
    double Clock,
    double ClockStepSeconds,
    IReadOnlyList<StorySimFlagDto> Flags,
    IReadOnlyList<StorySimNodeStateDto> Nodes,
    IReadOnlyList<StorySimInterventionDto> Interventions,
    IReadOnlyList<string> LuaNotifications,
    IReadOnlyList<string> Log,
    IReadOnlyList<StorySimStepDto> Steps,
    int TotalSteps,
    IReadOnlyList<string> Breakpoints,
    bool BreakOnGates,
    string? HaltedAt,
    StorySimWorldDto World,
    IReadOnlyList<StorySimLuaStateDto> LuaStates,
    // How many things the clock alone can still change (armed timers, owed completions, script
    // work). Zero: nothing more happens until the author answers a decision.
    int ClockPending = 0,
    // The battle this session runs, as the plots feed keys it; null for the galactic story.
    string? Scope = null,
    // Galactic only: the label of the battle whose session is up, while which the galaxy takes
    // no command - the game freezes the galaxy during a tactical battle.
    string? PausedFor = null,
    // Battle only: "won" or "lost" once resolved, after which the session takes no command.
    string? Outcome = null,
    // Galactic only: every battle of the faction in play order with its status - notStarted,
    // running (with its own tick), won or lost.
    IReadOnlyList<StorySimBattleDto>? Battles = null,
    // Whether a speech or movie a reward starts is owed its completion on the next tick (the
    // session's option at start); off, the listener waits for the author or the engine's timeout.
    bool AssumeMediaCompletes = true)
{
    public static StorySimStateDto NotRunning { get; } =
        new(false, 0, 0, 1, [], [], [], [], [], [], 0, [], false, null, StorySimWorldDto.Empty, []);
}

/// <param name="Status">
///     notStarted; pending (LINK_TACTICAL brought up the choice: fight or auto-resolve); fight (the
///     fight was chosen, the battle's panel opens into its session); autoResolve (the outcome is the
///     author's to decide, the galaxy keeps going meanwhile); running; won; lost.
/// </param>
/// <param name="Writes">
///     The flags the battle's own rewards can write, with the value each would set - offered on the
///     portal as picks when the battle is decided without being played.
/// </param>
public sealed record StorySimBattleDto(
    string Key,
    string Label,
    string Status,
    int Tick,
    IReadOnlyList<StorySimFlagDto>? Writes = null);

/// <summary>A campaign script's state machine: where it is, where it goes next, and the Story_Event calls it still owes.</summary>
public sealed record StorySimLuaStateDto(
    string ScriptUri,
    string ScriptName,
    string? Current,
    string? Next,
    IReadOnlyList<StorySimLuaPendingDto> Pending);

public sealed record StorySimLuaPendingDto(string Id, string State, double DueClock);

public sealed record StorySimFlagDto(string Name, int Value);

/// <summary>
///     <see cref="GateLabel" /> and <see cref="GateProgress" /> describe an armed event's clock
///     or flag gate ("4/10 s", "FLAG_X 2 of 3", 0..1); null for events with no such gate.
/// </summary>
public sealed record StorySimNodeStateDto(
    string NodeId,
    string Lifecycle,
    int FireCount,
    string? GateLabel = null,
    double? GateProgress = null);

/// <summary>
///     <see cref="Facet" /> is the world change kind that fires this event when its type reads
///     the world; <see cref="Suggested" /> is a ready change built from the event's own parameters.
/// </summary>
public sealed record StorySimInterventionDto(
    string Kind,
    string NodeId,
    string EventName,
    string? EventType,
    IReadOnlyList<string> Options,
    string? Facet,
    StorySimWorldChangeDto? Suggested,
    // A tactical decision's battle: the one it is inside, or the one whose entry it follows.
    string? BattleKey = null);

/// <summary>An author's change to the world; the fields a kind does not read stay null.</summary>
public sealed record StorySimWorldChangeDto(string Kind)
{
    public string? Planet { get; init; }
    public string? UnitType { get; init; }
    public string? Faction { get; init; }
    public string? Name { get; init; }
    public string? Mode { get; init; }
    public int Amount { get; init; } = 1;
    public IReadOnlyList<StorySimFlagDto>? Flags { get; init; }
    public string? NodeId { get; init; }
}

public sealed record StorySimPlanetDto(string Name, string? Owner, bool Revealed, bool Corrupted, bool Destroyed);

public sealed record StorySimUnitDto(string Type, string Owner, string Planet, int Count);

/// <summary>The fact table. Lists, never dictionaries, for the serializer's sake.</summary>
public sealed record StorySimWorldDto(
    IReadOnlyList<StorySimPlanetDto> Planets,
    IReadOnlyList<StorySimUnitDto> Units,
    IReadOnlyList<StorySimFlagDto> Tech,
    IReadOnlyList<StorySimFlagDto> Credits,
    string? Era,
    IReadOnlyList<StorySimFlagDto> Counters,
    IReadOnlyList<string> Objectives)
{
    public static StorySimWorldDto Empty { get; } = new([], [], [], [], null, [], []);
}

/// <summary>One trace transition; lifecycles travel as their enum names, null when the step is not a lifecycle change.</summary>
public sealed record StorySimStepDto(
    int Tick,
    int Seq,
    string NodeId,
    string? From,
    string? To,
    string? SourceNodeId,
    string Cause,
    string? Detail);

public sealed record StorySimStateResult(StorySimStateDto? State, string? Error = null);

// Every request names its session: the campaign faction and, for a battle panel, the battle's
// scope (the plots feed's key; null or empty is the galactic level). The scope is the last field
// on each record so a caller written before battles existed still lines up.

[Method("aet/storySimStart", Direction.ClientToServer)]
public sealed record StorySimStartParams(
    string Campaign,
    string Faction,
    string? Scope = null,
    // See StorySimStateDto.AssumeMediaCompletes; the session keeps it from start to stop. Nullable
    // because a missing field deserializes as false, never as the record's default; absent is on.
    bool? AssumeMediaCompletes = null)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimStop", Direction.ClientToServer)]
public sealed record StorySimStopParams(string Campaign, string Faction, string? Scope = null)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimGetState", Direction.ClientToServer)]
public sealed record StorySimGetStateParams(string Campaign, string Faction, int SinceSeq = 0, string? Scope = null)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimSatisfyTrigger", Direction.ClientToServer)]
public sealed record StorySimSatisfyTriggerParams(
    string Campaign,
    string Faction,
    string NodeId,
    int SinceSeq = 0,
    string? Scope = null)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimSetFlag", Direction.ClientToServer)]
public sealed record StorySimSetFlagParams(
    string Campaign,
    string Faction,
    string Flag,
    int Value,
    int SinceSeq = 0,
    string? Scope = null)
    : IRequest<StorySimStateResult>;

/// <summary>Kept for callers that think in seconds; one second is one tick.</summary>
[Method("aet/storySimAdvanceClock", Direction.ClientToServer)]
public sealed record StorySimAdvanceClockParams(
    string Campaign,
    string Faction,
    double Seconds,
    int SinceSeq = 0,
    string? Scope = null)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimLuaNotify", Direction.ClientToServer)]
public sealed record StorySimLuaNotifyParams(
    string Campaign,
    string Faction,
    string Id,
    int SinceSeq = 0,
    string? Scope = null)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimTick", Direction.ClientToServer)]
public sealed record StorySimTickParams(
    string Campaign,
    string Faction,
    int Count,
    int SinceSeq = 0,
    string? Scope = null)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimRunToDecision", Direction.ClientToServer)]
public sealed record StorySimRunToDecisionParams(
    string Campaign,
    string Faction,
    int SinceSeq = 0,
    string? Scope = null)
    : IRequest<StorySimStateResult>;

/// <summary>Replays the session to the state just after the given tick and drops everything after it.</summary>
[Method("aet/storySimSeek", Direction.ClientToServer)]
public sealed record StorySimSeekParams(string Campaign, string Faction, int Tick, string? Scope = null)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimWorld", Direction.ClientToServer)]
public sealed record StorySimWorldParams(
    string Campaign,
    string Faction,
    StorySimWorldChangeDto Change,
    int SinceSeq = 0,
    string? Scope = null)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimBreakpoints", Direction.ClientToServer)]
public sealed record StorySimBreakpointsParams(
    string Campaign,
    string Faction,
    IReadOnlyList<string> NodeIds,
    bool OnConditionalGates,
    string? Scope = null) : IRequest<StorySimStateResult>;

/// <summary>
///     Resolves a battle: won or lost. <c>Battle</c> is the battle's key; <c>Scope</c> is the
///     asking panel's own scope, whose state comes back - the galactic panel deciding on a portal,
///     or the battle panel deciding its own end.
/// </summary>
[Method("aet/storySimResolveBattle", Direction.ClientToServer)]
public sealed record StorySimResolveBattleParams(
    string Campaign,
    string Faction,
    string Battle,
    bool Won,
    int SinceSeq = 0,
    string? Scope = null,
    // The author's picks among the battle's possible flag writes; they cross with the outcome.
    IReadOnlyList<StorySimFlagDto>? Flags = null) : IRequest<StorySimStateResult>;

/// <summary>
///     Server -> client push after any simulation state change; the client whose panel shows that
///     campaign faction and scope re-fetches the state. A battle's resolution pushes both scopes.
/// </summary>
[Method("aet/storySimChanged", Direction.ServerToClient)]
public sealed record StorySimChangedParams(string Campaign, string Faction, string? Scope = null) : IRequest;