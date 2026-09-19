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
    string? HaltedAt)
{
    public static StorySimStateDto NotRunning { get; } =
        new(false, 0, 0, 1, [], [], [], [], [], [], 0, [], false, null);
}

public sealed record StorySimFlagDto(string Name, int Value);

public sealed record StorySimNodeStateDto(string NodeId, string Lifecycle, int FireCount);

public sealed record StorySimInterventionDto(
    string Kind,
    string NodeId,
    string EventName,
    string? EventType,
    IReadOnlyList<string> Options);

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

[Method("aet/storySimStart", Direction.ClientToServer)]
public sealed record StorySimStartParams(string Campaign, string Faction) : IRequest<StorySimStateResult>;

[Method("aet/storySimStop", Direction.ClientToServer)]
public sealed record StorySimStopParams(string Campaign, string Faction) : IRequest<StorySimStateResult>;

[Method("aet/storySimGetState", Direction.ClientToServer)]
public sealed record StorySimGetStateParams(string Campaign, string Faction, int SinceSeq = 0)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimSatisfyTrigger", Direction.ClientToServer)]
public sealed record StorySimSatisfyTriggerParams(string Campaign, string Faction, string NodeId, int SinceSeq = 0)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimSetFlag", Direction.ClientToServer)]
public sealed record StorySimSetFlagParams(string Campaign, string Faction, string Flag, int Value, int SinceSeq = 0)
    : IRequest<StorySimStateResult>;

/// <summary>Kept for callers that think in seconds; one second is one tick.</summary>
[Method("aet/storySimAdvanceClock", Direction.ClientToServer)]
public sealed record StorySimAdvanceClockParams(string Campaign, string Faction, double Seconds, int SinceSeq = 0)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimLuaNotify", Direction.ClientToServer)]
public sealed record StorySimLuaNotifyParams(string Campaign, string Faction, string Id, int SinceSeq = 0)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimTick", Direction.ClientToServer)]
public sealed record StorySimTickParams(string Campaign, string Faction, int Count, int SinceSeq = 0)
    : IRequest<StorySimStateResult>;

[Method("aet/storySimRunToDecision", Direction.ClientToServer)]
public sealed record StorySimRunToDecisionParams(string Campaign, string Faction, int SinceSeq = 0)
    : IRequest<StorySimStateResult>;

/// <summary>Replays the session to the state just after the given tick and drops everything after it.</summary>
[Method("aet/storySimSeek", Direction.ClientToServer)]
public sealed record StorySimSeekParams(string Campaign, string Faction, int Tick) : IRequest<StorySimStateResult>;

[Method("aet/storySimBreakpoints", Direction.ClientToServer)]
public sealed record StorySimBreakpointsParams(
    string Campaign,
    string Faction,
    IReadOnlyList<string> NodeIds,
    bool OnConditionalGates) : IRequest<StorySimStateResult>;

/// <summary>Server -> client push after any simulation state change; clients re-fetch the state.</summary>
[Method("aet/storySimChanged", Direction.ServerToClient)]
public sealed record StorySimChangedParams(string Campaign, string Faction) : IRequest;