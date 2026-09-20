// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Graph;

/// <summary>
///     One tactical battle of a campaign faction: the plot manifest a <c>LINK_TACTICAL</c>-style
///     event names, the events that link it in, and its rank in the order the galactic story
///     reaches them.
/// </summary>
/// <param name="Key">The scope key: the manifest file, chain-scanner-canonical and lower-cased.</param>
/// <param name="Label">The manifest's file name without its extension.</param>
/// <param name="EntryEventIds">The galactic events whose reward links this battle in.</param>
/// <param name="Rank">
///     0-based position in the order the galactic graph reaches the entry events, depth first from
///     the roots; ties broken by the entry event's document position. The order the player meets
///     the battles, not the order the files sort.
/// </param>
/// <param name="ThreadUris">The thread documents the manifest registers.</param>
public sealed record StoryBattle(
    string Key,
    string Label,
    IReadOnlyList<string> EntryEventIds,
    int Rank,
    IReadOnlySet<string> ThreadUris)
{
    /// <summary>The battle's plot files as its manifest writes them, in manifest order.</summary>
    public IReadOnlyList<string> ThreadFiles { get; init; } = [];
}

/// <summary>
///     Cuts the campaign graph into scopes: the galactic story, and one sub-graph per tactical
///     battle (decided 2026-09-20, see the plan). The whole graph stays what the analysis, the
///     diagnostics and the simulator work on; a scope is only what one panel shows.
///     <para>
///         Measured on the shipped Underworld campaign before this existed: 882 galactic events
///         in one thread against 1249 tactical ones in nine, and the only edges crossing the line
///         were 9 flag edges and 627 script links - zero prerequisites. So the galactic scope
///         keeps a battle as one portal node and folds every crossing edge onto it, and a battle
///         scope shows the galactic side as portals: the events that link it in and the events
///         that listen for its outcome. Script states appear in every scope that has listeners
///         for them, under the same id - that is how the game runs them.
///     </para>
/// </summary>
public static class StoryGraphScoper
{
    /// <summary>The event types that listen for a battle's outcome on the galactic side.</summary>
    private static readonly HashSet<string> OutcomeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "STORY_VICTORY", "STORY_MISSION_LOST", "STORY_MISSION_FAILED"
    };

    private const string TacticalPrefix = "tactical#";

    /// <summary>The scope key of a tactical manifest reference, the same normalisation the builder's stub id uses.</summary>
    public static string BattleKey(string manifestReference)
    {
        return StoryReferenceTypes.NormalizeRelativePath(manifestReference).ToLowerInvariant();
    }

    /// <summary>The galactic-side portal node id of a battle - the builder's tactical stub.</summary>
    public static string TacticalNodeId(string battleKey)
    {
        return TacticalPrefix + battleKey;
    }

    /// <summary>The id of a galactic event's stand-in inside a battle scope.</summary>
    public static string GalacticPortalId(string battleKey, string galacticNodeId)
    {
        return $"galactic#{battleKey}#{galacticNodeId}";
    }

    /// <summary>Whether a scope key names the galactic scope.</summary>
    public static bool IsGalactic(string? scope)
    {
        return string.IsNullOrEmpty(scope);
    }

    /// <summary>
    ///     The faction's battles, ranked in the order the galactic story reaches them.
    ///     <paramref name="tacticalManifestFiles" /> carries each manifest's plot files as written,
    ///     for the battle's own listing; absent, a battle names its threads by URI alone.
    /// </summary>
    public static IReadOnlyList<StoryBattle> Battles(StoryGraph graph,
        IReadOnlyDictionary<string, IReadOnlySet<string>> tacticalManifestThreads,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? tacticalManifestFiles = null)
    {
        if (tacticalManifestThreads.Count == 0) return [];

        var tacticalThreads = TacticalThreads(tacticalManifestThreads);
        var nodesById = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var order = GalacticVisitOrder(graph, nodesById, tacticalThreads);

        var battles = new List<(StoryBattle Battle, int Visit, int Line)>();
        foreach (var (manifest, threads) in tacticalManifestThreads)
        {
            var key = BattleKey(manifest);
            var stubId = TacticalNodeId(key);
            var entries = graph.Edges
                .Where(e => e.Kind == StoryEdgeKind.Tactical && e.ToId == stubId)
                .Select(e => e.FromId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var visit = entries.Count == 0
                ? int.MaxValue
                : entries.Min(id => order.GetValueOrDefault(id, int.MaxValue));
            var line = entries.Count == 0
                ? int.MaxValue
                : entries.Min(id => nodesById.GetValueOrDefault(id)?.Event?.Range.StartLine ?? int.MaxValue);
            var label = Path.GetFileNameWithoutExtension(StoryReferenceTypes.NormalizeRelativePath(manifest));
            var files = tacticalManifestFiles?.GetValueOrDefault(manifest) ?? [];
            battles.Add((new StoryBattle(key, label, entries, 0, threads) { ThreadFiles = files }, visit, line));
        }

        return battles
            .OrderBy(b => b.Visit).ThenBy(b => b.Line).ThenBy(b => b.Battle.Key, StringComparer.Ordinal)
            .Select((b, rank) => b.Battle with { Rank = rank })
            .ToList();
    }

    /// <summary>
    ///     The graph one panel shows: the galactic story when <paramref name="scope" /> is null or
    ///     empty, else the battle with that key. An unknown key yields an empty graph rather than
    ///     the whole campaign, so a stale tab shows nothing instead of the wrong thing.
    /// </summary>
    public static StoryGraph Scope(StoryCampaignModel model, string? scope)
    {
        var graph = model.Graph;
        var manifests = model.TacticalManifestThreads;
        if (manifests.Count == 0)
            return IsGalactic(scope) ? graph : new StoryGraph([], [], graph.Problems);

        var tacticalThreads = TacticalThreads(manifests);
        var battleOfThread = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (manifest, threads) in manifests)
        foreach (var thread in threads)
            battleOfThread.TryAdd(thread, BattleKey(manifest));

        if (IsGalactic(scope)) return GalacticScope(graph, tacticalThreads, battleOfThread);

        var key = BattleKey(scope!);
        var battleThreads = manifests
            .Where(kvp => BattleKey(kvp.Key) == key)
            .SelectMany(kvp => kvp.Value)
            .ToHashSet(StringComparer.Ordinal);
        return battleThreads.Count == 0
            ? new StoryGraph([], [], graph.Problems)
            : BattleScope(graph, key, battleThreads, tacticalThreads);
    }

    // ── Galactic ─────────────────────────────────────────────────────────────

    private static StoryGraph GalacticScope(StoryGraph graph, HashSet<string> tacticalThreads,
        Dictionary<string, string> battleOfThread)
    {
        var nodesById = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        // In: everything whose thread is not a battle's, plus the battle stubs themselves. Script
        // states are decided by their edges below.
        bool InScope(StoryNode n)
        {
            return n.Kind switch
            {
                StoryNodeKind.TacticalPlot => true,
                StoryNodeKind.LuaState => false,
                _ => n.ThreadUri is null || !tacticalThreads.Contains(n.ThreadUri)
            };
        }

        string? BattleOf(StoryNode n)
        {
            return n.ThreadUri is not null && battleOfThread.TryGetValue(n.ThreadUri, out var k) ? k : null;
        }

        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
            if (InScope(node))
                kept.Add(node.Id);

        var edges = new List<StoryEdge>();
        var edgeSet = new HashSet<(string, string, StoryEdgeKind, string?)>();
        var usedScripts = new HashSet<string>(StringComparer.Ordinal);

        void Add(StoryEdge e)
        {
            if (e.FromId == e.ToId) return; // a folded edge that landed on its own portal
            if (edgeSet.Add((e.FromId, e.ToId, e.Kind, e.Label))) edges.Add(e);
        }

        foreach (var e in graph.Edges)
        {
            if (!nodesById.TryGetValue(e.FromId, out var from) || !nodesById.TryGetValue(e.ToId, out var to)) continue;
            var fromIn = kept.Contains(e.FromId) || from.Kind == StoryNodeKind.LuaState;
            var toIn = kept.Contains(e.ToId) || to.Kind == StoryNodeKind.LuaState;
            if (from.Kind == StoryNodeKind.LuaState && to.Kind == StoryNodeKind.LuaState)
            {
                // A state-to-state link belongs to whichever scope shows both states; the pass
                // below adds it once the states are known to be used here.
                continue;
            }

            if (fromIn && toIn)
            {
                if (from.Kind == StoryNodeKind.LuaState) usedScripts.Add(from.Id);
                if (to.Kind == StoryNodeKind.LuaState) usedScripts.Add(to.Id);
                Add(e);
                continue;
            }

            // The battle's stub stands in for every node of that battle. The entry edges from the
            // stub into the battle fold onto the stub itself and vanish.
            var fromId = fromIn ? e.FromId : BattleOf(from) is { } fk ? TacticalNodeId(fk) : null;
            var toId = toIn ? e.ToId : BattleOf(to) is { } tk ? TacticalNodeId(tk) : null;
            if (fromId is null || toId is null) continue;
            if (from.Kind == StoryNodeKind.LuaState) usedScripts.Add(from.Id);
            if (to.Kind == StoryNodeKind.LuaState) usedScripts.Add(to.Id);
            Add(e with { FromId = fromId, ToId = toId });
        }

        // The engine's own links from a battle to what follows it, so the sequence the game plays
        // reads in order: the stub to the galactic listeners for its outcome and for the summary
        // dialog closing. Drawn, not prerequisites - the simulator raises both on resolution.
        foreach (var key in battleOfThread.Values.Distinct(StringComparer.Ordinal))
        {
            var stubId = TacticalNodeId(key);
            if (!nodesById.ContainsKey(stubId)) continue;
            var entries = graph.Edges
                .Where(e => e.Kind == StoryEdgeKind.Tactical && e.ToId == stubId)
                .Select(e => e.FromId)
                .Distinct(StringComparer.Ordinal);
            foreach (var entryId in entries)
            foreach (var (node, summary) in BattleEndDependants(graph, nodesById, entryId))
                Add(new StoryEdge(stubId, node.Id, StoryEdgeKind.Implicit, summary ? "summary closed" : "outcome"));
        }

        AddScriptTransitions(graph, usedScripts, Add);
        kept.UnionWith(usedScripts);

        return new StoryGraph(
            graph.Nodes.Where(n => kept.Contains(n.Id)).ToList(),
            edges,
            graph.Problems);
    }

    // ── Battle ───────────────────────────────────────────────────────────────

    private static StoryGraph BattleScope(StoryGraph graph, string key, HashSet<string> battleThreads,
        HashSet<string> tacticalThreads)
    {
        var nodesById = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var stubId = TacticalNodeId(key);

        bool InScope(StoryNode n)
        {
            return n.Kind is not (StoryNodeKind.TacticalPlot or StoryNodeKind.LuaState)
                   && n.ThreadUri is not null && battleThreads.Contains(n.ThreadUri);
        }

        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
            if (InScope(node))
                kept.Add(node.Id);

        var nodes = new List<StoryNode>();
        var portals = new Dictionary<string, StoryNode>(StringComparer.Ordinal);

        StoryNode Portal(StoryNode galactic)
        {
            var id = GalacticPortalId(key, galactic.Id);
            if (!portals.TryGetValue(id, out var portal))
            {
                portal = new StoryNode(id, StoryNodeKind.GalacticPortal, galactic.Label, null)
                {
                    PortalTarget = galactic.Id
                };
                portals[id] = portal;
            }

            return portal;
        }

        var edges = new List<StoryEdge>();
        var edgeSet = new HashSet<(string, string, StoryEdgeKind, string?)>();
        var usedScripts = new HashSet<string>(StringComparer.Ordinal);

        void Add(StoryEdge e)
        {
            if (e.FromId == e.ToId) return;
            if (edgeSet.Add((e.FromId, e.ToId, e.Kind, e.Label))) edges.Add(e);
        }

        // The events that link this battle in: sources of the Tactical edges into its stub.
        var entries = graph.Edges
            .Where(e => e.Kind == StoryEdgeKind.Tactical && e.ToId == stubId)
            .Select(e => nodesById.GetValueOrDefault(e.FromId))
            .OfType<StoryNode>()
            .ToList();

        foreach (var e in graph.Edges)
        {
            if (!nodesById.TryGetValue(e.FromId, out var from) || !nodesById.TryGetValue(e.ToId, out var to)) continue;
            var fromIn = kept.Contains(e.FromId) || from.Kind == StoryNodeKind.LuaState;
            var toIn = kept.Contains(e.ToId) || to.Kind == StoryNodeKind.LuaState;
            if (from.Kind == StoryNodeKind.LuaState && to.Kind == StoryNodeKind.LuaState) continue;
            if (!fromIn && !toIn) continue;

            if (fromIn && toIn)
            {
                if (from.Kind == StoryNodeKind.LuaState) usedScripts.Add(from.Id);
                if (to.Kind == StoryNodeKind.LuaState) usedScripts.Add(to.Id);
                Add(e);
                continue;
            }

            // The stub's own entry edges become the entry portals' edges, one per linking event.
            if (e.Kind == StoryEdgeKind.TacticalEntry && e.FromId == stubId)
            {
                foreach (var entry in entries)
                    Add(e with { FromId = Portal(entry).Id });
                continue;
            }

            // Anything else crossing the line is another battle's or the galaxy's; a galactic event
            // gets its portal, another battle's node is not this graph's business.
            var far = fromIn ? to : from;
            if (far.Kind is StoryNodeKind.TacticalPlot) continue;
            if (far.ThreadUri is not null && tacticalThreads.Contains(far.ThreadUri)) continue;
            if (far.Kind is StoryNodeKind.LuaState) continue;
            if (from.Kind == StoryNodeKind.LuaState) usedScripts.Add(from.Id);
            if (to.Kind == StoryNodeKind.LuaState) usedScripts.Add(to.Id);
            var portal = Portal(far);
            Add(fromIn ? e with { ToId = portal.Id } : e with { FromId = portal.Id });
        }

        // Exit portals: the galactic listeners for this battle's outcome, read as the outcome-typed
        // dependants of the linking events. Inferred from the shipped data's shape (a victory
        // listener sits right behind its LINK_TACTICAL event), not measured in the engine, which
        // fires them on the tactical result regardless of position.
        foreach (var entry in entries)
        {
            Portal(entry);
            foreach (var listener in OutcomeDependants(graph, nodesById, entry.Id))
                Add(new StoryEdge(Portal(entry).Id, Portal(listener).Id, StoryEdgeKind.Tactical, "outcome"));
        }

        AddScriptTransitions(graph, usedScripts, Add);
        kept.UnionWith(usedScripts);

        nodes.AddRange(graph.Nodes.Where(n => kept.Contains(n.Id)));
        nodes.AddRange(portals.Values);
        return new StoryGraph(nodes, edges, graph.Problems);
    }

    // ── Shared ───────────────────────────────────────────────────────────────

    private static HashSet<string> TacticalThreads(IReadOnlyDictionary<string, IReadOnlySet<string>> manifests)
    {
        return manifests.Values.SelectMany(v => v).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>State-to-state links, added once both ends are states this scope shows.</summary>
    private static void AddScriptTransitions(StoryGraph graph, HashSet<string> usedScripts, Action<StoryEdge> add)
    {
        foreach (var e in graph.Edges)
            if (e.Kind == StoryEdgeKind.LuaLink && usedScripts.Contains(e.FromId) && usedScripts.Contains(e.ToId))
                add(e);
    }

    /// <summary>
    ///     Depth-first visit order over the galactic part of the graph from its roots (events with
    ///     no incoming prerequisite or control edge), following prerequisite and control edges
    ///     through junctions. Roots in document order: thread order as assembled, then line.
    /// </summary>
    private static Dictionary<string, int> GalacticVisitOrder(StoryGraph graph,
        Dictionary<string, StoryNode> nodesById, HashSet<string> tacticalThreads)
    {
        bool Galactic(StoryNode n)
        {
            return n.Kind != StoryNodeKind.LuaState && n.Kind != StoryNodeKind.TacticalPlot
                                                    && (n.ThreadUri is null || !tacticalThreads.Contains(n.ThreadUri));
        }

        var next = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var hasIncoming = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in graph.Edges)
        {
            if (e.Kind is not (StoryEdgeKind.Prereq or StoryEdgeKind.Control)) continue;
            if (!nodesById.TryGetValue(e.FromId, out var from) || !nodesById.TryGetValue(e.ToId, out var to)) continue;
            if (!Galactic(from) || !Galactic(to)) continue;
            if (!next.TryGetValue(e.FromId, out var list)) next[e.FromId] = list = [];
            list.Add(e.ToId);
            hasIncoming.Add(e.ToId);
        }

        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        var counter = 0;
        var stack = new Stack<string>();
        foreach (var root in graph.Nodes.Where(n =>
                     n.Kind == StoryNodeKind.Event && Galactic(n) && !hasIncoming.Contains(n.Id)))
        {
            stack.Push(root.Id);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (order.ContainsKey(id)) continue;
                order[id] = counter++;
                if (!next.TryGetValue(id, out var children)) continue;
                // Pushed in reverse so the first child is visited first.
                for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
            }
        }

        return order;
    }

    /// <summary>Outcome-typed events reachable from the entry event by prerequisite edges through junctions.</summary>
    private static IEnumerable<StoryNode> OutcomeDependants(StoryGraph graph,
        Dictionary<string, StoryNode> nodesById, string entryId)
    {
        return BattleEndDependants(graph, nodesById, entryId).Where(d => !d.Summary).Select(d => d.Node);
    }

    /// <summary>
    ///     Whether an event is a STORY_GENERIC listener for the battle summary dialog closing,
    ///     which the tutorial puts between the link and its outcome listeners.
    /// </summary>
    private static bool IsSummaryListener(StoryNode node)
    {
        return node.Event is { } storyEvent
               && string.Equals(storyEvent.EventType, "STORY_GENERIC", StringComparison.OrdinalIgnoreCase)
               && StoryGraphBuilder.NameTokens(storyEvent.EventParams.FirstOrDefault(p => p.Position == 0)?.RawValue)
                   .Contains(Sim.StorySimulator.BattleEndClosed);
    }

    /// <summary>
    ///     The galactic listeners behind a battle's link event: the outcome listeners, and the
    ///     summary-closed listeners the walk also passes through, since an outcome may sit behind
    ///     one. Any other event ends the walk - the outcome sits right behind the link, not behind
    ///     the rest of the act.
    /// </summary>
    private static IEnumerable<(StoryNode Node, bool Summary)> BattleEndDependants(StoryGraph graph,
        Dictionary<string, StoryNode> nodesById, string entryId)
    {
        var next = graph.Edges
            .Where(e => e.Kind == StoryEdgeKind.Prereq)
            .ToLookup(e => e.FromId, e => e.ToId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { entryId };
        var queue = new Queue<string>([entryId]);
        while (queue.Count > 0)
        {
            foreach (var id in next[queue.Dequeue()])
            {
                if (!seen.Add(id) || !nodesById.TryGetValue(id, out var node)) continue;
                if (node.Kind != StoryNodeKind.Event)
                {
                    queue.Enqueue(id); // through a junction
                    continue;
                }

                if (IsSummaryListener(node))
                {
                    yield return (node, true);
                    queue.Enqueue(id);
                }
                else if (node.Event?.EventType is { } type && OutcomeTypes.Contains(type))
                {
                    yield return (node, false);
                }
            }
        }
    }
}