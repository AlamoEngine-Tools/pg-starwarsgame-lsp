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
// bookkeeping. No dictionaries in the DTOs: OmniSharp's serializer camelCases dictionary string
// keys, which would corrupt flag names and node ids.

public sealed record StorySimStateDto(
    bool Running,
    double Clock,
    IReadOnlyList<StorySimFlagDto> Flags,
    IReadOnlyList<StorySimNodeStateDto> Nodes,
    IReadOnlyList<StorySimInterventionDto> Interventions,
    IReadOnlyList<string> LuaNotifications,
    IReadOnlyList<string> Log);

public sealed record StorySimFlagDto(string Name, int Value);

public sealed record StorySimNodeStateDto(string NodeId, string Lifecycle);

public sealed record StorySimInterventionDto(
    string Kind,
    string NodeId,
    string EventName,
    string? EventType,
    IReadOnlyList<string> Options);

public sealed record StorySimStateResult(StorySimStateDto? State, string? Error = null);

[Method("aet/storySimStart", Direction.ClientToServer)]
public sealed record StorySimStartParams(string Campaign) : IRequest<StorySimStateResult>;

[Method("aet/storySimStop", Direction.ClientToServer)]
public sealed record StorySimStopParams(string Campaign) : IRequest<StorySimStateResult>;

[Method("aet/storySimGetState", Direction.ClientToServer)]
public sealed record StorySimGetStateParams(string Campaign) : IRequest<StorySimStateResult>;

[Method("aet/storySimSatisfyTrigger", Direction.ClientToServer)]
public sealed record StorySimSatisfyTriggerParams(string Campaign, string NodeId) : IRequest<StorySimStateResult>;

[Method("aet/storySimSetFlag", Direction.ClientToServer)]
public sealed record StorySimSetFlagParams(string Campaign, string Flag, int Value) : IRequest<StorySimStateResult>;

[Method("aet/storySimAdvanceClock", Direction.ClientToServer)]
public sealed record StorySimAdvanceClockParams(string Campaign, double Seconds) : IRequest<StorySimStateResult>;

[Method("aet/storySimLuaNotify", Direction.ClientToServer)]
public sealed record StorySimLuaNotifyParams(string Campaign, string Id) : IRequest<StorySimStateResult>;

/// <summary>Server → client push after any simulation state change; clients re-fetch the state.</summary>
[Method("aet/storySimChanged", Direction.ServerToClient)]
public sealed record StorySimChangedParams(string Campaign) : IRequest;