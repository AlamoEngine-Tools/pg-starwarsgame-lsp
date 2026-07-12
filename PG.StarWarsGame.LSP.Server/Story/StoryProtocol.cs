// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     Shared gating for every <c>aet/getStory*</c> endpoint: results carry the message naming
///     exactly the setting to flip. Dependent flags do not auto-enable their prerequisites —
///     a storyEditor request with discovery off names the missing discovery flag.
/// </summary>
public static class StoryEditorFeature
{
    public const string DisabledMessage =
        "The story editor is disabled. Enable 'aet-eaw-edit.features.tools.storyEditor' in the editor settings.";

    public const string DiscoveryMissingMessage =
        "The story editor requires story discovery. Enable 'aet-eaw-edit.features.story.discovery' in the editor settings.";

    public static string? Rejection(ILspConfigurationProvider config)
    {
        if (!config.Current.Features.Tools.StoryEditor) return DisabledMessage;
        if (!config.Current.Features.Story.Discovery) return DiscoveryMissingMessage;
        return null;
    }
}

// ── aet/getStoryPlots — campaign navigator feed ──────────────────────────────

[Method("aet/getStoryPlots", Direction.ClientToServer)]
public sealed record GetStoryPlotsParams : IRequest<GetStoryPlotsResult>;

public sealed record GetStoryPlotsResult(IReadOnlyList<StoryCampaignDto> Campaigns, string? Error = null);

public sealed record StoryCampaignDto(string Name, IReadOnlyList<StoryFactionDto> Factions);

public sealed record StoryFactionDto(
    string Faction,
    string ManifestFile,
    IReadOnlyList<StoryPlotThreadDto> Threads,
    IReadOnlyList<StoryLuaScriptDto> LuaScripts);

/// <param name="Name">Extensionless script name exactly as written in the plot manifest.</param>
/// <param name="Uri">
///     Canonical URI of the indexed <c>.lua</c> document, or null when no indexed script matches.
///     Same casing rationale as <see cref="StoryPlotThreadDto.Uri" />.
/// </param>
public sealed record StoryLuaScriptDto(string Name, string? Uri = null);

/// <param name="File">Display name exactly as written in the plot manifest (engine casing).</param>
/// <param name="Uri">
///     Resolved canonical document URI, or null when the chain is broken. Manifest entries and
///     on-disk names differ in casing throughout vanilla data (the engine resolves files
///     case-insensitively) — clients must open this URI instead of searching for
///     <paramref name="File" />.
/// </param>
public sealed record StoryPlotThreadDto(string File, bool Suspended, string? Uri = null);

// ── aet/getStoryGraph — filtered campaign graph ──────────────────────────────

[Method("aet/getStoryGraph", Direction.ClientToServer)]
public sealed record GetStoryGraphParams(
    string Campaign,
    string? NameFilter = null,
    string? Branch = null,
    string? Lifecycle = null,
    string? ReachableFrom = null) : IRequest<GetStoryGraphResult>;

public sealed record GetStoryGraphResult(
    IReadOnlyList<StoryGraphNodeDto> Nodes,
    IReadOnlyList<StoryGraphEdgeDto> Edges,
    string? Error = null);

public sealed record StoryGraphNodeDto(
    string Id,
    string Kind,
    string Label,
    string? ThreadUri,
    int? Line,
    string? EventType,
    string? RewardType,
    string? Branch,
    string? Lifecycle,
    bool Reachable);

public sealed record StoryGraphEdgeDto(string FromId, string ToId, string Kind, string? Label);

// ── aet/getStoryNodeDetail — full event payload for the property view ────────

[Method("aet/getStoryNodeDetail", Direction.ClientToServer)]
public sealed record GetStoryNodeDetailParams(string Campaign, string NodeId)
    : IRequest<GetStoryNodeDetailResult>;

public sealed record GetStoryNodeDetailResult(StoryNodeDetailDto? Node, string? Error = null);

public sealed record StoryNodeDetailDto(
    string Id,
    string Name,
    string? ThreadUri,
    int Line,
    string? EventType,
    string? EventFilter,
    IReadOnlyList<StoryParamValueDto> EventParams,
    string? RewardType,
    IReadOnlyList<StoryParamValueDto> RewardParams,
    IReadOnlyList<IReadOnlyList<string>> PrereqGroups,
    string? Branch,
    bool Perpetual,
    string? StoryDialog,
    int? StoryChapter,
    IReadOnlyList<StoryTagDto> Tags);

public sealed record StoryParamValueDto(int Position, string Value);

public sealed record StoryTagDto(string Name, string Value);

// ── aet/getStorySchema — the client hardcodes nothing ────────────────────────

[Method("aet/getStorySchema", Direction.ClientToServer)]
public sealed record GetStorySchemaParams : IRequest<GetStorySchemaResult>;

public sealed record GetStorySchemaResult(
    IReadOnlyList<StoryTypeSchemaDto> EventTypes,
    IReadOnlyList<StoryTypeSchemaDto> RewardTypes,
    string? Error = null);

public sealed record StoryTypeSchemaDto(
    string Name,
    string? Description,
    bool Untested,
    IReadOnlyList<StoryParamSchemaDto> Params);

public sealed record StoryParamSchemaDto(
    int Position,
    string ValueType,
    string? ReferenceType,
    string? EnumName,
    bool Optional,
    string? Description);

// ── aet/storyGraphChanged — server push after model invalidation ─────────────

[Method("aet/storyGraphChanged", Direction.ServerToClient)]
public sealed record StoryGraphChangedParams(IReadOnlyList<string> Campaigns) : IRequest;
