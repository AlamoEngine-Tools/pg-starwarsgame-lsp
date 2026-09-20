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
    TacticalPlot,

    /// <summary>A <c>StoryModeEvents</c> state of a campaign Lua script; its id is the script uri plus the state.</summary>
    LuaState,

    /// <summary>
    ///     A galactic event seen from inside a battle's graph: the event that links the battle in,
    ///     or one that listens for its outcome. <see cref="StoryNode.PortalTarget" /> names it.
    /// </summary>
    GalacticPortal
}

public enum StoryEdgeKind
{
    Prereq,
    Control,
    Tactical,
    Flag,

    /// <summary>Reserved: XML ↔ Lua couplings; produced once the Lua artifacts exist (Issue 3).</summary>
    LuaLink,

    /// <summary>
    ///     A tactical plot node to a root event (no incoming Prereq/Control) of one of the
    ///     threads its manifest includes - lets the editor jump from the stub into that
    ///     battle's own story via the existing reachable-from traversal.
    /// </summary>
    TacticalEntry,

    /// <summary>
    ///     A link the engine makes between a reward and the listeners it will reach, drawn so a
    ///     sequence the game plays in order reads in order: a speech or movie to the listener that
    ///     waits for it to end, a battle's stub to the galactic listeners for its outcome and for
    ///     the summary dialog closing. Never a prerequisite - the evaluator ignores it, the
    ///     simulator follows the same link through its world changes. The label names the link.
    /// </summary>
    Implicit
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
    StoryEvent? Event = null)
{
    /// <summary>
    ///     For a portal, the id of the node it stands for in the other scope: a
    ///     <see cref="StoryNodeKind.GalacticPortal" /> names a galactic event, a
    ///     <see cref="StoryNodeKind.TacticalPlot" /> names nothing here (its battle key is in its id).
    /// </summary>
    public string? PortalTarget { get; init; }
}

public sealed record StoryEdge(string FromId, string ToId, StoryEdgeKind Kind, string? Label = null);

public enum StoryGraphProblemKind
{
    DanglingPrereq,
    UnresolvedControlTarget,
    AmbiguousTarget,

    /// <summary>
    ///     A STORY_GENERIC listener whose names the game never raises (measured: 36 names from
    ///     the engine's call sites, see <see cref="StoryGenericNames" />) and which no
    ///     TRIGGER_EVENT pushes: it never fires.
    /// </summary>
    GenericNeverRaised
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