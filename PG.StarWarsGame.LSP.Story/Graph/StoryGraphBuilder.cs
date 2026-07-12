// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Graph;

/// <summary>
///     Builds the campaign-wide story graph from parsed threads. Edge extraction is
///     schema-driven: which param slots produce which edges comes from the <c>referenceType</c>
///     annotations on the <c>StoryEventType</c>/<c>StoryRewardType</c> enums
///     (<c>StoryEventName</c> → control, <c>StoryFlag</c> → flag, <c>StoryPlotFile</c> →
///     tactical, <c>StoryBranch</c> → control to every branch member) — no hardcoded per-type
///     switch. Event names resolve campaign-wide (<c>TRIGGER_EVENT</c> is campaign-global);
///     ambiguity is tracked, not guessed away.
/// </summary>
public sealed class StoryGraphBuilder(ISchemaProvider schema)
{
    private const string RefStoryEventName = StoryReferenceTypes.EventName;
    private const string RefStoryFlag = StoryReferenceTypes.Flag;
    private const string RefStoryPlotFile = StoryReferenceTypes.PlotFile;
    private const string RefStoryBranch = StoryReferenceTypes.Branch;

    public StoryGraph Build(IReadOnlyList<StoryThread> threads)
    {
        var state = new BuildState(
            StoryReferenceTypes.BuildParamMap(schema.GetEnum("StoryEventType")),
            StoryReferenceTypes.BuildParamMap(schema.GetEnum("StoryRewardType")));

        // Pass 1: event nodes and the campaign-wide name index. Duplicate names within one file
        // are a diagnostic elsewhere; here they get deterministic disambiguated ids so the graph
        // stays well-formed.
        var eventNodes = new List<(StoryThread Thread, StoryNode Node)>();
        var idCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var thread in threads)
        foreach (var storyEvent in thread.Events)
        {
            var baseId = EventNodeId(thread.DocumentUri, storyEvent.Name);
            var occurrence = idCounts.GetValueOrDefault(baseId);
            idCounts[baseId] = occurrence + 1;
            var id = occurrence == 0 ? baseId : $"{baseId}#{occurrence + 1}";

            var node = new StoryNode(id, StoryNodeKind.Event, storyEvent.Name, thread.DocumentUri, storyEvent);
            state.Nodes.Add(node);
            eventNodes.Add((thread, node));
            if (!state.EventsByName.TryGetValue(storyEvent.Name, out var list))
                state.EventsByName[storyEvent.Name] = list = [];
            list.Add(node);
            if (storyEvent.Branch is not null)
            {
                if (!state.EventsByBranch.TryGetValue(storyEvent.Branch, out var members))
                    state.EventsByBranch[storyEvent.Branch] = members = [];
                members.Add(node);
            }
        }

        // Pass 2: edges (needs the complete name index for campaign-global resolution).
        foreach (var (thread, node) in eventNodes)
        {
            var storyEvent = node.Event!;
            AddPrereqEdges(state, thread, storyEvent, node);
            AddParamEdges(state, thread, storyEvent, node, storyEvent.EventType,
                storyEvent.EventParams, state.EventParamRefTypes);
            AddParamEdges(state, thread, storyEvent, node, storyEvent.RewardType,
                storyEvent.RewardParams, state.RewardParamRefTypes);
        }

        // Pass 3: flag data-flow edges (writers feed readers, campaign-wide).
        foreach (var (flag, readerId) in state.FlagReaders)
        foreach (var (writerFlag, writerId, display) in state.FlagWriters)
        {
            if (!flag.Equals(writerFlag, StringComparison.OrdinalIgnoreCase) || writerId == readerId)
                continue;
            state.AddEdge(new StoryEdge(writerId, readerId, StoryEdgeKind.Flag, display));
        }

        return new StoryGraph(state.Nodes, state.Edges, state.Problems);
    }

    // ── Prereqs: OR of AND-lines, materialized as junctions ──────────────────

    private static void AddPrereqEdges(BuildState state, StoryThread thread, StoryEvent storyEvent, StoryNode node)
    {
        var groups = storyEvent.PrereqGroups;
        if (groups.Count == 0) return;

        var sinkId = node.Id;
        if (groups.Count > 1)
        {
            var orJunction = new StoryNode($"{node.Id}#or", StoryNodeKind.OrJunction, "OR", thread.DocumentUri);
            state.Nodes.Add(orJunction);
            state.AddEdge(new StoryEdge(orJunction.Id, node.Id, StoryEdgeKind.Prereq));
            sinkId = orJunction.Id;
        }

        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            var groupSinkId = sinkId;
            if (group.Tokens.Count > 1)
            {
                var andJunction = new StoryNode($"{node.Id}#g{groupIndex}",
                    StoryNodeKind.AndJunction, "AND", thread.DocumentUri);
                state.Nodes.Add(andJunction);
                state.AddEdge(new StoryEdge(andJunction.Id, sinkId, StoryEdgeKind.Prereq));
                groupSinkId = andJunction.Id;
            }

            foreach (var token in group.Tokens)
            foreach (var source in ResolveEventName(state, thread, token.Text, token.Range,
                         StoryGraphProblemKind.DanglingPrereq,
                         $"Prerequisite '{token.Text}' does not match any event in this campaign."))
                state.AddEdge(new StoryEdge(source.Id, groupSinkId, StoryEdgeKind.Prereq));
        }
    }

    // ── Schema-driven param edges ────────────────────────────────────────────

    private void AddParamEdges(BuildState state, StoryThread thread, StoryEvent storyEvent,
        StoryNode node, string? typeName, IReadOnlyList<StoryParamSlot> slots,
        IReadOnlyDictionary<(string, int), string> refTypes)
    {
        if (typeName is null) return;

        foreach (var slot in slots)
        {
            if (slot.RawValue.Length == 0) continue;
            if (!refTypes.TryGetValue((typeName.ToUpperInvariant(), slot.Position), out var refType)) continue;

            switch (refType)
            {
                case RefStoryEventName:
                    AddControlEdges(state, thread, node, typeName, slot);
                    break;
                case RefStoryFlag:
                    // Trigger-side flag params are reads, reward-side ones are writes.
                    var isRead = ReferenceEquals(refTypes, state.EventParamRefTypes);
                    foreach (var flag in SplitList(slot.RawValue))
                        if (isRead)
                            state.FlagReaders.Add((flag, node.Id));
                        else
                            state.FlagWriters.Add((flag, node.Id, flag));
                    break;
                case RefStoryPlotFile:
                    var tactical = state.GetOrAddTacticalNode(slot.RawValue);
                    state.AddEdge(new StoryEdge(node.Id, tactical.Id, StoryEdgeKind.Tactical, typeName));
                    break;
                case RefStoryBranch:
                    if (state.EventsByBranch.TryGetValue(slot.RawValue, out var members))
                        foreach (var member in members)
                            state.AddEdge(new StoryEdge(node.Id, member.Id, StoryEdgeKind.Control, typeName));
                    break;
            }
        }
    }

    private static void AddControlEdges(BuildState state, StoryThread thread, StoryNode node,
        string typeName, StoryParamSlot slot)
    {
        foreach (var target in ResolveEventName(state, thread, slot.RawValue, slot.Range,
                     StoryGraphProblemKind.UnresolvedControlTarget,
                     $"'{slot.RawValue}' does not match any event in this campaign."))
            if (target.ThreadUri == thread.DocumentUri)
            {
                state.AddEdge(new StoryEdge(node.Id, target.Id, StoryEdgeKind.Control, typeName));
            }
            else
            {
                // Cross-file targets route through a portal owned by the source thread, so a
                // single-file rendering has a stable stand-in for the remote event.
                var portal = state.GetOrAddPortal(thread.DocumentUri, target);
                state.AddEdge(new StoryEdge(node.Id, portal.Id, StoryEdgeKind.Control, typeName));
                state.AddEdge(new StoryEdge(portal.Id, target.Id, StoryEdgeKind.Control, typeName));
            }
    }

    // Campaign-wide, case-insensitive event-name resolution. Zero matches records the given
    // problem; multiple matches record an ambiguity AND yield every match — the graph shows all
    // candidates instead of guessing.
    private static IReadOnlyList<StoryNode> ResolveEventName(BuildState state, StoryThread thread,
        string name, StorySourceRange range, StoryGraphProblemKind unresolvedKind, string unresolvedMessage)
    {
        if (!state.EventsByName.TryGetValue(name, out var matches) || matches.Count == 0)
        {
            state.Problems.Add(new StoryGraphProblem(unresolvedKind, thread.DocumentUri, range,
                name, unresolvedMessage));
            return [];
        }

        if (matches.Count > 1)
            state.Problems.Add(new StoryGraphProblem(StoryGraphProblemKind.AmbiguousTarget,
                thread.DocumentUri, range, name,
                $"'{name}' matches {matches.Count} events in this campaign — the engine's pick is undefined."));

        return matches;
    }

    private static string EventNodeId(string documentUri, string eventName)
    {
        return $"{documentUri}#{eventName.ToLowerInvariant()}";
    }

    private static IEnumerable<string> SplitList(string rawValue)
    {
        return StoryReferenceTypes.SplitList(rawValue);
    }

    private sealed class BuildState(
        Dictionary<(string, int), string> eventParamRefTypes,
        Dictionary<(string, int), string> rewardParamRefTypes)
    {
        private readonly Dictionary<string, StoryNode> _portals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, StoryNode> _tacticalNodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<(string, string, StoryEdgeKind, string?)> _edgeSet = [];

        public Dictionary<(string, int), string> EventParamRefTypes { get; } = eventParamRefTypes;
        public Dictionary<(string, int), string> RewardParamRefTypes { get; } = rewardParamRefTypes;

        public List<StoryNode> Nodes { get; } = [];
        public List<StoryEdge> Edges { get; } = [];
        public List<StoryGraphProblem> Problems { get; } = [];

        public Dictionary<string, List<StoryNode>> EventsByName { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<StoryNode>> EventsByBranch { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<(string Flag, string NodeId)> FlagReaders { get; } = [];
        public List<(string Flag, string NodeId, string Display)> FlagWriters { get; } = [];

        public void AddEdge(StoryEdge edge)
        {
            if (_edgeSet.Add((edge.FromId, edge.ToId, edge.Kind, edge.Label)))
                Edges.Add(edge);
        }

        public StoryNode GetOrAddPortal(string sourceThreadUri, StoryNode target)
        {
            var id = $"{sourceThreadUri}#portal#{target.Id}";
            if (_portals.TryGetValue(id, out var existing)) return existing;
            var portal = new StoryNode(id, StoryNodeKind.Portal, target.Label, sourceThreadUri);
            _portals[id] = portal;
            Nodes.Add(portal);
            return portal;
        }

        public StoryNode GetOrAddTacticalNode(string plotFileReference)
        {
            var key = plotFileReference.Replace('\\', '/').ToLowerInvariant();
            if (_tacticalNodes.TryGetValue(key, out var existing)) return existing;
            var node = new StoryNode($"tactical#{key}", StoryNodeKind.TacticalPlot, plotFileReference, null);
            _tacticalNodes[key] = node;
            Nodes.Add(node);
            return node;
        }
    }
}
