// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Graph;

public enum StoryNodeKind
{
    Event,

    /// <summary>Virtual: all inputs of one prereq line must have fired (AND).</summary>
    AndJunction,

    /// <summary>Virtual: any one prereq line arms the event (OR across lines).</summary>
    OrJunction,

    /// <summary>Virtual: stand-in rendered in the source thread for a cross-file target.</summary>
    Portal,

    /// <summary>A tactical plot manifest attached via STORY_*_TACTICAL / LINK_TACTICAL.</summary>
    TacticalPlot
}

public enum StoryEdgeKind
{
    Prereq,
    Control,
    Tactical,
    Flag,

    /// <summary>Reserved: XML ↔ Lua couplings; produced once the Lua artifacts exist (Issue 3).</summary>
    LuaLink
}

/// <summary>
///     A graph node. Event nodes carry their backing <see cref="StoryEvent" />; virtual nodes
///     (junctions, portals) have deterministic ids derived from their owning event so layouts
///     stay stable across rebuilds.
/// </summary>
public sealed record StoryNode(
    string Id,
    StoryNodeKind Kind,
    string Label,
    string? ThreadUri,
    StoryEvent? Event = null);

public sealed record StoryEdge(string FromId, string ToId, StoryEdgeKind Kind, string? Label = null);

public enum StoryGraphProblemKind
{
    DanglingPrereq,
    UnresolvedControlTarget,
    AmbiguousTarget
}

/// <summary>A resolution defect found while building the graph, anchored to the referencing value.</summary>
public sealed record StoryGraphProblem(
    StoryGraphProblemKind Kind,
    string DocumentUri,
    StorySourceRange Range,
    string Reference,
    string Message);

/// <summary>The campaign-wide story graph: every thread's events plus virtual nodes and typed edges.</summary>
public sealed record StoryGraph(
    IReadOnlyList<StoryNode> Nodes,
    IReadOnlyList<StoryEdge> Edges,
    IReadOnlyList<StoryGraphProblem> Problems)
{
    public static readonly StoryGraph Empty = new([], [], []);
}
