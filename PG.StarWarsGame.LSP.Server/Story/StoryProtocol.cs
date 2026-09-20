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

/// <summary>
///     Gating for the authoring endpoints behind Edit mode - the staged-batch preview and the writes
///     themselves. Layered on top of <see cref="StoryEditorFeature" />: the panel must be on before
///     editing within it means anything, and whichever flag is missing is the one named.
///     <para>
///         Note that <c>aet/validateStoryCommandBatch</c> is deliberately NOT here: validating
///         writes nothing and the Validate button is available in every mode, so it belongs to the
///         read surface.
///     </para>
/// </summary>
public static class StoryEditingFeature
{
    public const string DisabledMessage =
        "Story editing is disabled. Enable 'aet-eaw-edit.features.tools.storyEditing' in the editor settings.";

    public static string? Rejection(ILspConfigurationProvider config)
    {
        if (StoryEditorFeature.Rejection(config) is { } rejection) return rejection;
        if (!config.Current.Features.Tools.StoryEditing) return DisabledMessage;
        return null;
    }
}

// ── aet/getStoryPlots - campaign navigator feed ──────────────────────────────

[Method("aet/getStoryPlots", Direction.ClientToServer)]
public sealed record GetStoryPlotsParams : IRequest<GetStoryPlotsResult>;

public sealed record GetStoryPlotsResult(IReadOnlyList<StoryCampaignDto> Campaigns, string? Error = null);

// Set is the Campaign_Set grouping value (a referenceGroup key) this campaign belongs to, sourced
// from the workspace group index; null when the campaign declares no Campaign_Set. The navigator
// groups campaigns by it (set-less campaigns fall under an "Ungrouped" node).
public sealed record StoryCampaignDto(string Name, IReadOnlyList<StoryFactionDto> Factions, string? Set = null);

public sealed record StoryFactionDto(
    string Faction,
    string ManifestFile,
    IReadOnlyList<StoryPlotThreadDto> Threads,
    IReadOnlyList<StoryLuaScriptDto> LuaScripts,
    // The faction's tactical battles in the order the galactic story reaches them; each opens as
    // its own graph through getStoryGraph's Scope.
    IReadOnlyList<StoryBattleDto>? Battles = null);

/// <param name="Key">The scope key for getStoryGraph.</param>
/// <param name="Label">The plot manifest's file name without extension.</param>
/// <param name="EntryEventIds">The galactic event node ids whose reward links the battle in.</param>
/// <param name="Rank">0-based order in which the galactic story reaches the battle.</param>
/// <param name="Threads">The battle's thread document URIs.</param>
/// <param name="Threads">
///     The battle's plot files as its tactical manifest lists them, each with the document it
///     resolved to. The faction's own thread list never carries these.
/// </param>
/// <param name="LuaScripts">
///     The scripts the battle's tactical manifest attaches, resolved like the faction's own; the
///     faction's script list never carries these either.
/// </param>
public sealed record StoryBattleDto(
    string Key,
    string Label,
    IReadOnlyList<string> EntryEventIds,
    int Rank,
    IReadOnlyList<StoryPlotThreadDto> Threads,
    IReadOnlyList<StoryLuaScriptDto>? LuaScripts = null);

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
///     case-insensitively) - clients must open this URI instead of searching for
///     <paramref name="File" />.
/// </param>
public sealed record StoryPlotThreadDto(string File, bool Suspended, string? Uri = null);

// ── aet/getStoryGraph - filtered campaign graph ──────────────────────────────

[Method("aet/getStoryGraph", Direction.ClientToServer)]
public sealed record GetStoryGraphParams(
    string Campaign,
    string Faction,
    string? NameFilter = null,
    string? Branch = null,
    string? Lifecycle = null,
    string? ReachableFrom = null,
    // Whether the faction manifest registers a plot as Active_Plot or Suspended_Plot.
    // Null or empty keeps both, which is the whole chain.
    string? PlotState = null,
    // Which way <paramref name="ReachableFrom" /> reaches: "Downstream" (what the event leads to),
    // "Upstream" (what leads to it) or "Both". Null or unrecognised is Downstream, which is what the
    // filter did before the other two directions existed - so an older client keeps its behaviour.
    string? ReachableDirection = null,
    // Which scope to show: null or empty is the galactic story with each battle as one portal;
    // a battle key (from getStoryPlots' battles) is that battle's own graph with the galaxy as
    // portals. An older client sends none and gets the galactic story.
    string? Scope = null) : IRequest<GetStoryGraphResult>;

/// <param name="Branches">
///     Every branch name in the campaign, INDEPENDENT of the filters that produced
///     <paramref name="Nodes" />. A facet list has to describe what the user could switch to, so it
///     cannot be derived from the filtered result: reading it off the returned nodes leaves the
///     branch dropdown holding only the branch already selected, which is the one option that is
///     of no use. The server resolves the filter, so the server is what knows the full set.
/// </param>
/// <param name="Threads">
///     Every thread URI in the campaign, likewise unfiltered - it backs the thread picker for
///     placing a NEW event, which must be able to target a thread the current filter hides.
/// </param>
public sealed record GetStoryGraphResult(
    IReadOnlyList<StoryGraphNodeDto> Nodes,
    IReadOnlyList<StoryGraphEdgeDto> Edges,
    string? Error = null,
    IReadOnlyList<string>? Branches = null,
    IReadOnlyList<string>? Threads = null);

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
    bool Reachable,
    IReadOnlyList<StoryParamValueDto>? EventParams = null,
    IReadOnlyList<StoryParamValueDto>? RewardParams = null,
    bool Perpetual = false,
    string? StoryDialog = null,
    int? StoryChapter = null,
    // For a GalacticPortal: the galactic event it stands for, so the client can jump to it.
    string? PortalTarget = null);

public sealed record StoryGraphEdgeDto(string FromId, string ToId, string Kind, string? Label);

// ── aet/getStoryNodeDetail - full event payload for the property view ────────

[Method("aet/getStoryNodeDetail", Direction.ClientToServer)]
public sealed record GetStoryNodeDetailParams(string Campaign, string Faction, string NodeId)
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

// ── aet/getStorySchema - the client hardcodes nothing ────────────────────────

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
    string? Description,
    // The enum's value names, shipped inline so enum params render as dropdowns without a
    // round trip. Null for non-enum params.
    IReadOnlyList<string>? EnumValues = null);

// ── aet/getStoryParamOptions - completion candidates for one param slot ──────

/// <param name="Side"><c>event</c> or <c>reward</c>.</param>
/// <param name="TypeName">The event/reward type whose param schema applies.</param>
/// <param name="Position">0-based param slot.</param>
[Method("aet/getStoryParamOptions", Direction.ClientToServer)]
public sealed record GetStoryParamOptionsParams(
    string Campaign,
    string Faction,
    string Side,
    string TypeName,
    int Position,
    string? Prefix = null,
    int? Limit = null) : IRequest<GetStoryParamOptionsResult>;

public sealed record GetStoryParamOptionsResult(
    IReadOnlyList<StoryParamOptionDto> Options,
    string? Error = null);

public sealed record StoryParamOptionDto(string Value, string? Detail = null);

// ── aet/resolveStoryReference - go-to for a reference-typed param value ──────

[Method("aet/resolveStoryReference", Direction.ClientToServer)]
public sealed record ResolveStoryReferenceParams(string Value, string? ReferenceType = null)
    : IRequest<ResolveStoryReferenceResult>;

public sealed record ResolveStoryReferenceResult(
    string? Uri = null,
    int Line = 0,
    int Column = 0,
    string? Error = null);

// ── aet/getStoryDiagnostics - validation results correlated to graph nodes ───

[Method("aet/getStoryDiagnostics", Direction.ClientToServer)]
public sealed record GetStoryDiagnosticsParams(string Campaign, string Faction) : IRequest<GetStoryDiagnosticsResult>;

public sealed record GetStoryDiagnosticsResult(
    IReadOnlyList<StoryDiagnosticDto> Diagnostics,
    string? Error = null);

/// <param name="NodeId">The graph node the diagnostic belongs to; null for file-level problems.</param>
/// <param name="Side"><c>event</c>/<c>reward</c> when the range falls inside a param slot.</param>
/// <param name="Position">0-based param slot, when <paramref name="Side" /> is set.</param>
public sealed record StoryDiagnosticDto(
    string? NodeId,
    string? Side,
    int? Position,
    string Severity,
    string Message,
    string Uri,
    int Line,
    int Column);

// ── aet/getStoryLayout / aet/setStoryLayout - node position sidecar ──────────

[Method("aet/getStoryLayout", Direction.ClientToServer)]
/// <param name="Scope">
///     The battle whose panel asks, or null for the galactic level: a battle graph's portals exist
///     only in that scope, and the answer names the nodes the panel holds.
/// </param>
public sealed record GetStoryLayoutParams(string Campaign, string Faction, string? Scope = null)
    : IRequest<GetStoryLayoutResult>;

public sealed record GetStoryLayoutResult(IReadOnlyList<StoryLayoutEntryDto> Entries, string? Error = null);

[Method("aet/setStoryLayout", Direction.ClientToServer)]
public sealed record SetStoryLayoutParams(string Campaign, string Faction, IReadOnlyList<StoryLayoutEntryDto> Entries)
    : IRequest<SetStoryLayoutResult>;

public sealed record SetStoryLayoutResult(bool Success, string? Error = null);

/// <param name="NodeId">
///     Set for a virtual node - a junction, a portal, a tactical stub, a script state - which the
///     sidecar names by id; the thread and event are then empty. Absent for an event.
/// </param>
public sealed record StoryLayoutEntryDto(string ThreadUri, string EventName, double X, double Y, string? NodeId = null);

// ── aet/storyGraphChanged - server push after model invalidation ─────────────

[Method("aet/storyGraphChanged", Direction.ServerToClient)]
public sealed record StoryGraphChangedParams(IReadOnlyList<string> Campaigns) : IRequest;