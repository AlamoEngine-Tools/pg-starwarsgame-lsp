// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Graph;

/// <summary>
///     Builds the campaign-wide story graph from parsed threads. Edge extraction is
///     schema-driven: which param slots produce which edges comes from the <c>referenceType</c>
///     annotations on the <c>StoryEventType</c>/<c>StoryRewardType</c> enums
///     (<c>StoryEventName</c> → control, <c>StoryFlag</c> → flag, <c>StoryPlotFile</c> →
///     tactical, <c>StoryBranch</c> → control to every branch member) - no hardcoded per-type
///     switch.
///     <para>
///         Names resolve the way the engine resolves them (measured): a prerequisite, a branch, a
///         <c>RESET_*</c> target and a <c>DISABLE_*</c> target are looked up in the event's own
///         subplot - one thread file - and a name the file does not hold is an assert in the game,
///         so here it is a diagnostic and never an edge. <c>TRIGGER_EVENT</c>, and
///         <c>DISABLE_STORY_EVENT</c> with a non-zero third parameter, act on the name in EVERY
///         subplot, so several matches are several edges rather than an ambiguity. The one
///         ambiguity left is two events of one name in one file, which the engine cannot tell
///         apart either.
///     </para>
/// </summary>
public sealed class StoryGraphBuilder(ISchemaProvider schema)
{
    private const string RefStoryEventName = StoryReferenceTypes.EventName;
    private const string RefStoryFlag = StoryReferenceTypes.Flag;
    private const string RefStoryPlotFile = StoryReferenceTypes.PlotFile;
    private const string RefStoryBranch = StoryReferenceTypes.Branch;

    /// <param name="threads">Every thread assembled into the campaign (main plots and tactical plots alike).</param>
    /// <param name="tacticalManifestThreads">
    ///     Manifest file (chain-scanner-canonical, e.g. <see cref="StoryReferenceTypes.NormalizeRelativePath" />)
    ///     → the document URIs of the threads it includes. Drives the <see cref="StoryEdgeKind.TacticalEntry" />
    ///     pass linking each tactical stub to its battle's own root events.
    /// </param>
    public StoryGraph Build(IReadOnlyList<StoryThread> threads,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? tacticalManifestThreads = null,
        IReadOnlyList<LuaStoryMachine>? luaMachines = null)
    {
        var state = new BuildState(
            StoryReferenceTypes.BuildParamMap(schema.GetEnum("StoryEventType")),
            StoryReferenceTypes.BuildParamMap(schema.GetEnum("StoryRewardType")));

        // Pass 1: event nodes and the name indexes, one campaign-wide and one per thread.
        // Duplicate names within one file are a diagnostic elsewhere; here they get deterministic
        // disambiguated ids so the graph stays well-formed.
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
            var threadKey = (thread.DocumentUri, storyEvent.Name.ToLowerInvariant());
            if (!state.EventsByThreadAndName.TryGetValue(threadKey, out var inThread))
                state.EventsByThreadAndName[threadKey] = inThread = [];
            inThread.Add(node);
            if (storyEvent.Branch is not null)
            {
                // Per thread, and case-sensitive: the engine compares branch names with a plain
                // string equality and only ever walks its own subplot's events.
                var branchKey = (thread.DocumentUri, storyEvent.Branch);
                if (!state.EventsByBranch.TryGetValue(branchKey, out var members))
                    state.EventsByBranch[branchKey] = members = [];
                members.Add(node);
            }
        }

        // Pass 2: edges (needs the complete name indexes).
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

        // Pass 3b: media completion edges - a reward that starts a speech or a movie to the
        // listeners that wait for it to end, campaign-wide, since the engine dispatches the
        // completion by name to every active listener. Measured on the corpus: 1485 of 1522
        // speech-done listeners name a MULTIMEDIA speech (parameter 8), 35 a SPEECH reward's.
        // Drawn, not a prerequisite: the simulator owes the completion the same way.
        var mediaListeners = new List<(string Kind, HashSet<string> Names, string NodeId)>();
        foreach (var (_, node) in eventNodes)
        {
            var kind = node.Event!.EventType?.ToUpperInvariant() switch
            {
                "STORY_SPEECH_DONE" => "speech",
                "STORY_MOVIE_DONE" => "movie",
                _ => null
            };
            if (kind is null) continue;
            var names = NameTokens(node.Event.EventParams.FirstOrDefault(p => p.Position == 0)?.RawValue);
            if (names.Count > 0) mediaListeners.Add((kind, names, node.Id));
        }

        foreach (var (_, node) in eventNodes)
        {
            var (kind, position) = node.Event!.RewardType?.ToUpperInvariant() switch
            {
                "SPEECH" => ("speech", 0),
                "MULTIMEDIA" => ("speech", 7),
                "START_MOVIE" => ("movie", 0),
                _ => (null, 0)
            };
            if (kind is null) continue;
            var name = node.Event.RewardParams.FirstOrDefault(p => p.Position == position)?.RawValue.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            foreach (var listener in mediaListeners)
                if (listener.Kind == kind && listener.Names.Contains(name) && listener.NodeId != node.Id)
                    state.AddEdge(new StoryEdge(node.Id, listener.NodeId, StoryEdgeKind.Implicit, kind));
        }

        // Pass 4: tactical entry edges - root events (no incoming Prereq/Control) of a tactical
        // manifest's own threads get an edge from that manifest's stub node, so "reachable from
        // here" can jump straight into the battle's story.
        if (tacticalManifestThreads is { Count: > 0 })
        {
            var hasIncoming = new HashSet<string>(
                state.Edges.Where(e => e.Kind is StoryEdgeKind.Prereq or StoryEdgeKind.Control)
                    .Select(e => e.ToId),
                StringComparer.Ordinal);
            foreach (var (manifestFile, threadUris) in tacticalManifestThreads)
            {
                var tactical = state.GetOrAddTacticalNode(manifestFile);
                foreach (var (thread, node) in eventNodes)
                    if (threadUris.Contains(thread.DocumentUri) && !hasIncoming.Contains(node.Id))
                        state.AddEdge(new StoryEdge(tactical.Id, node.Id, StoryEdgeKind.TacticalEntry));
            }
        }

        // Pass 5: the campaign scripts as state nodes. An XML event whose name is a StoryModeEvents
        // key selects that state (Story_Event_Trigger); a state's phases emit Story_Event ids that
        // STORY_AI_NOTIFICATION events listen for; a Set_Next_State is a state-to-state link.
        foreach (var machine in luaMachines ?? [])
        {
            foreach (var luaState in machine.States)
                state.Nodes.Add(new StoryNode(LuaStateNodeId(machine.ScriptUri, luaState.Name), StoryNodeKind.LuaState,
                    luaState.Name, machine.ScriptUri));

            foreach (var luaState in machine.States)
            {
                var stateId = LuaStateNodeId(machine.ScriptUri, luaState.Name);
                if (state.EventsByName.TryGetValue(luaState.Name, out var triggers))
                    foreach (var trigger in triggers)
                        state.AddEdge(new StoryEdge(trigger.Id, stateId, StoryEdgeKind.LuaLink, "Story_Event_Trigger"));

                var emitted = new[] { luaState.OnEnter, luaState.OnUpdate, luaState.OnExit }
                    .SelectMany(p => p.Emissions.Select(e => e.Id))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var id in emitted)
                foreach (var (_, node) in eventNodes)
                    if (ListensFor(state, node.Event!, id))
                        state.AddEdge(new StoryEdge(stateId, node.Id, StoryEdgeKind.LuaLink, id));

                foreach (var target in new[] { luaState.OnEnter, luaState.OnUpdate, luaState.OnExit }
                             .SelectMany(p => p.Transitions).Distinct(StringComparer.OrdinalIgnoreCase))
                    if (machine.States.Any(s => s.Name.Equals(target, StringComparison.OrdinalIgnoreCase)))
                        state.AddEdge(new StoryEdge(stateId, LuaStateNodeId(machine.ScriptUri, target),
                            StoryEdgeKind.LuaLink, "Set_Next_State"));
            }
        }

        return new StoryGraph(state.Nodes, state.Edges, state.Problems);
    }

    /// <summary>The node id of a script state: the script uri plus the state name, lower-cased like an event id.</summary>
    /// <summary>A string-list parameter's names, split as the engine splits (whitespace and commas), compared case-insensitively.</summary>
    public static HashSet<string> NameTokens(string? raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (raw is null) return set;
        foreach (var token in raw.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries))
            set.Add(token);
        return set;
    }

    public static string LuaStateNodeId(string scriptUri, string stateName)
    {
        return $"{scriptUri}#lua#{stateName.ToLowerInvariant()}";
    }

    private static bool ListensFor(BuildState state, StoryEvent storyEvent, string notificationId)
    {
        var type = storyEvent.EventType?.ToUpperInvariant();
        if (type is null) return false;
        foreach (var slot in storyEvent.EventParams)
        {
            if (state.EventParamRefTypes.GetValueOrDefault((type, slot.Position)) != StoryReferenceTypes.Notification)
                continue;
            if (StoryReferenceTypes.SplitList(slot.RawValue)
                .Any(id => id.Equals(notificationId, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
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
                         StoryGraphProblemKind.DanglingPrereq, "Prerequisite", campaignWide: false))
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
                    AddControlEdges(state, thread, node, typeName, slot, ReachesEverySubplot(typeName, slots));
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
                    if (state.EventsByBranch.TryGetValue((thread.DocumentUri, slot.RawValue), out var members))
                        foreach (var member in members)
                            state.AddEdge(new StoryEdge(node.Id, member.Id, StoryEdgeKind.Control, typeName));
                    break;
            }
        }
    }

    /// <summary>
    ///     Measured: <c>TRIGGER_EVENT</c> runs on the name in every subplot; <c>DISABLE_STORY_EVENT</c>
    ///     does so only when its third parameter parses non-zero (atoi); every other control reward
    ///     stays inside its own subplot.
    /// </summary>
    private static bool ReachesEverySubplot(string typeName, IReadOnlyList<StoryParamSlot> slots)
    {
        switch (typeName.ToUpperInvariant())
        {
            case "TRIGGER_EVENT":
                return true;
            case "DISABLE_STORY_EVENT":
                var third = slots.FirstOrDefault(s => s.Position == 2)?.RawValue.Trim();
                return third is not null && LeadingInt(third) != 0;
            default:
                return false;
        }
    }

    /// <summary>atoi: the leading integer of the text, or 0.</summary>
    private static int LeadingInt(string text)
    {
        var end = 0;
        if (end < text.Length && (text[end] == '-' || text[end] == '+')) end++;
        while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
        return int.TryParse(text[..end], out var value) ? value : 0;
    }

    private static void AddControlEdges(BuildState state, StoryThread thread, StoryNode node,
        string typeName, StoryParamSlot slot, bool campaignWide)
    {
        foreach (var target in ResolveEventName(state, thread, slot.RawValue, slot.Range,
                     StoryGraphProblemKind.UnresolvedControlTarget, $"{typeName} target", campaignWide))
            if (target.ThreadUri is { } targetThread && DocumentUris.Same(targetThread, thread.DocumentUri))
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

    // Case-insensitive event-name resolution, in the referencing event's own thread unless the
    // reward reaches every subplot. Zero matches records the given problem - naming the file that
    // does hold the name, when one does, because that is the fix the author is looking for. Two
    // events of one name in one file record an ambiguity AND yield both: the graph shows every
    // candidate instead of guessing, and the engine cannot tell them apart either.
    private static IReadOnlyList<StoryNode> ResolveEventName(BuildState state, StoryThread thread,
        string name, StorySourceRange range, StoryGraphProblemKind unresolvedKind, string what, bool campaignWide)
    {
        var everywhere = state.EventsByName.GetValueOrDefault(name) ?? [];
        var matches = campaignWide
            ? everywhere
            : state.EventsByThreadAndName.GetValueOrDefault((thread.DocumentUri, name.ToLowerInvariant())) ?? [];

        if (matches.Count == 0)
        {
            var elsewhere = everywhere
                .Select(n => n.ThreadUri)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Select(FileNameOf)
                .ToList();
            var message = everywhere.Count == 0
                ? $"{what} '{name}' does not match any event in this campaign."
                : $"{what} '{name}' is not an event in this plot file - the engine only looks here and asserts. " +
                  $"An event of that name is in {string.Join(", ", elsewhere)}.";
            state.Problems.Add(new StoryGraphProblem(unresolvedKind, thread.DocumentUri, range, name, message));
            return [];
        }

        var sameFileTwins = matches
            .GroupBy(n => n.ThreadUri, StringComparer.Ordinal)
            .Any(g => g.Count() > 1);
        if (sameFileTwins)
            state.Problems.Add(new StoryGraphProblem(StoryGraphProblemKind.AmbiguousTarget,
                thread.DocumentUri, range, name,
                $"'{name}' names more than one event in the same plot file - the engine's pick is undefined."));

        return matches;
    }

    private static string FileNameOf(string documentUri)
    {
        var slash = documentUri.LastIndexOf('/');
        return slash >= 0 ? documentUri[(slash + 1)..] : documentUri;
    }

    /// <summary>
    ///     The canonical graph-node id for an event - deterministic from its thread URI and name, so
    ///     the client and the diagnostics correlator can compute the same id for an event the model
    ///     has not been rebuilt from yet (e.g. one just created in a staged edit-mode batch).
    /// </summary>
    public static string EventNodeId(string documentUri, string eventName)
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
        private readonly HashSet<(string, string, StoryEdgeKind, string?)> _edgeSet = [];
        private readonly Dictionary<string, StoryNode> _portals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, StoryNode> _tacticalNodes = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<(string, int), string> EventParamRefTypes { get; } = eventParamRefTypes;
        public Dictionary<(string, int), string> RewardParamRefTypes { get; } = rewardParamRefTypes;

        public List<StoryNode> Nodes { get; } = [];
        public List<StoryEdge> Edges { get; } = [];
        public List<StoryGraphProblem> Problems { get; } = [];

        /// <summary>Every event of a name across the campaign - what a campaign-wide reward acts on.</summary>
        public Dictionary<string, List<StoryNode>> EventsByName { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The events of a name in one thread (thread uri, lower-cased name) - the engine's subplot lookup.</summary>
        public Dictionary<(string ThreadUri, string LowerName), List<StoryNode>> EventsByThreadAndName { get; } = new();

        /// <summary>Branch members per thread (thread uri, branch as written) - the engine's case-sensitive walk.</summary>
        public Dictionary<(string ThreadUri, string Branch), List<StoryNode>> EventsByBranch { get; } = new();

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
            var key = StoryReferenceTypes.NormalizeRelativePath(plotFileReference).ToLowerInvariant();
            if (_tacticalNodes.TryGetValue(key, out var existing)) return existing;
            var node = new StoryNode($"tactical#{key}", StoryNodeKind.TacticalPlot, plotFileReference, null);
            _tacticalNodes[key] = node;
            Nodes.Add(node);
            return node;
        }
    }
}