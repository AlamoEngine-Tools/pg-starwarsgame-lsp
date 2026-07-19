// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Graph;

public enum StoryEventLifecycle
{
    /// <summary>The owning thread is suspended - the event cannot participate yet.</summary>
    Inactive,

    /// <summary>Prerequisites are not satisfied.</summary>
    Waiting,

    /// <summary>Prerequisites satisfied - the trigger condition can trip.</summary>
    Armed,

    /// <summary>Fired and not perpetual.</summary>
    Fired,

    /// <summary>Explicitly disabled (DISABLE_STORY_EVENT / DISABLE_BRANCH).</summary>
    Disabled
}

/// <summary>
///     Immutable story runtime state - which events fired, which are disabled, which threads are
///     suspended, and the flag table. Shared by static analysis (reachability, filtering) today
///     and the simulator sessions later; every mutation returns a new state.
/// </summary>
public sealed record StoryRuntimeState
{
    public static readonly StoryRuntimeState Initial = new();

    public ImmutableHashSet<string> FiredEvents { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    public ImmutableHashSet<string> DisabledEvents { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    public ImmutableHashSet<string> SuspendedThreads { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    public ImmutableDictionary<string, int> Flags { get; init; } =
        ImmutableDictionary.Create<string, int>(StringComparer.OrdinalIgnoreCase);

    public StoryRuntimeState WithFired(string eventNodeId)
    {
        return this with { FiredEvents = FiredEvents.Add(eventNodeId) };
    }

    public StoryRuntimeState WithDisabled(string eventNodeId)
    {
        return this with { DisabledEvents = DisabledEvents.Add(eventNodeId) };
    }

    public StoryRuntimeState WithSuspendedThread(string threadUri)
    {
        return this with { SuspendedThreads = SuspendedThreads.Add(threadUri) };
    }

    public StoryRuntimeState WithFlag(string name, int value)
    {
        return this with { Flags = Flags.SetItem(name, value) };
    }
}

/// <summary>
///     The executable side of the story model: prereq evaluation (OR of AND-lines), event
///     lifecycle, static reachability, and prereq-cycle detection over a built
///     <see cref="StoryGraph" />. Used for graph diagnostics and filtering now; the simulator
///     (Issue 9) drives the same evaluator with evolving <see cref="StoryRuntimeState" />s.
/// </summary>
public sealed class StoryEvaluator
{
    private readonly ILookup<string, StoryEdge> _controlEdgesByFrom;
    private readonly Dictionary<string, StoryNode> _eventNodesById;
    private readonly Dictionary<string, List<StoryNode>> _eventsByName;

    public StoryEvaluator(StoryGraph graph)
    {
        _eventNodesById = graph.Nodes
            .Where(n => n.Kind == StoryNodeKind.Event)
            .ToDictionary(n => n.Id, StringComparer.Ordinal);
        _eventsByName = new Dictionary<string, List<StoryNode>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in _eventNodesById.Values)
        {
            if (!_eventsByName.TryGetValue(node.Event!.Name, out var list))
                _eventsByName[node.Event.Name] = list = [];
            list.Add(node);
        }

        _controlEdgesByFrom = graph.Edges
            .Where(e => e.Kind == StoryEdgeKind.Control)
            .ToLookup(e => e.FromId, StringComparer.Ordinal);
    }

    public StoryEventLifecycle GetLifecycle(string eventNodeId, StoryRuntimeState state)
    {
        var node = _eventNodesById[eventNodeId];

        if (state.DisabledEvents.Contains(eventNodeId))
            return StoryEventLifecycle.Disabled;
        if (node.ThreadUri is not null && state.SuspendedThreads.Contains(node.ThreadUri))
            return StoryEventLifecycle.Inactive;
        if (state.FiredEvents.Contains(eventNodeId) && !node.Event!.Perpetual)
            return StoryEventLifecycle.Fired;

        return PrereqsSatisfied(node.Event!, state)
            ? StoryEventLifecycle.Armed
            : StoryEventLifecycle.Waiting;
    }

    /// <summary>
    ///     OR over prereq lines, AND within a line. An ambiguous name counts as satisfied when
    ///     ANY event carrying it has fired (the engine's pick is undefined; the ambiguity itself
    ///     is a separate diagnostic).
    /// </summary>
    public bool PrereqsSatisfied(StoryEvent storyEvent, StoryRuntimeState state)
    {
        if (storyEvent.PrereqGroups.Count == 0) return true;

        foreach (var group in storyEvent.PrereqGroups)
        {
            var allFired = group.Tokens.Count > 0;
            foreach (var token in group.Tokens)
                if (!_eventsByName.TryGetValue(token.Text, out var matches)
                    || !matches.Any(m => state.FiredEvents.Contains(m.Id)))
                {
                    allFired = false;
                    break;
                }

            if (allFired) return true;
        }

        return false;
    }

    /// <summary>
    ///     Fixpoint reachability: an event is reachable when its prereqs can be satisfied through
    ///     reachable events (any OR-line whose tokens all resolve to at least one reachable
    ///     event), or when a reachable event force-fires it through a control edge
    ///     (TRIGGER_EVENT and friends). Prereq cycles without an external trigger stay
    ///     unreachable.
    /// </summary>
    public IReadOnlySet<string> ComputeReachableEvents()
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var controlTargetsOf = BuildControlTargets();

        bool changed;
        do
        {
            changed = false;
            foreach (var node in _eventNodesById.Values)
            {
                if (reachable.Contains(node.Id)) continue;
                if (!IsSatisfiableFrom(node.Event!, reachable) && !controlTargetsOf(node.Id, reachable))
                    continue;
                reachable.Add(node.Id);
                changed = true;
            }
        } while (changed);

        return reachable;
    }

    /// <summary>Strongly connected components of size &gt; 1 in the prereq reference graph.</summary>
    public IReadOnlyList<IReadOnlyList<string>> FindPrereqCycles()
    {
        // Tarjan over event nodes; prereq tokens are the incoming references.
        var index = 0;
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var cycles = new List<IReadOnlyList<string>>();

        foreach (var node in _eventNodesById.Values)
            if (!indices.ContainsKey(node.Id))
                StrongConnect(node.Id);

        return cycles;

        void StrongConnect(string id)
        {
            indices[id] = lowLinks[id] = index++;
            stack.Push(id);
            onStack.Add(id);

            foreach (var dependency in PrereqDependenciesOf(id))
                if (!indices.ContainsKey(dependency))
                {
                    StrongConnect(dependency);
                    lowLinks[id] = Math.Min(lowLinks[id], lowLinks[dependency]);
                }
                else if (onStack.Contains(dependency))
                {
                    lowLinks[id] = Math.Min(lowLinks[id], indices[dependency]);
                }

            if (lowLinks[id] != indices[id]) return;

            var component = new List<string>();
            string member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                component.Add(member);
            } while (member != id);

            if (component.Count > 1)
                cycles.Add(component);
        }
    }

    private IEnumerable<string> PrereqDependenciesOf(string eventNodeId)
    {
        var storyEvent = _eventNodesById[eventNodeId].Event!;
        foreach (var group in storyEvent.PrereqGroups)
        foreach (var token in group.Tokens)
            if (_eventsByName.TryGetValue(token.Text, out var matches))
                foreach (var match in matches)
                    yield return match.Id;
    }

    private bool IsSatisfiableFrom(StoryEvent storyEvent, HashSet<string> reachable)
    {
        if (storyEvent.PrereqGroups.Count == 0) return true;

        foreach (var group in storyEvent.PrereqGroups)
        {
            var allSatisfiable = group.Tokens.Count > 0;
            foreach (var token in group.Tokens)
                if (!_eventsByName.TryGetValue(token.Text, out var matches)
                    || !matches.Any(m => reachable.Contains(m.Id)))
                {
                    allSatisfiable = false;
                    break;
                }

            if (allSatisfiable) return true;
        }

        return false;
    }

    // Control edges may route through portals; resolve "is this event force-fired by a reachable
    // event" by following one portal hop back to the real source.
    private Func<string, HashSet<string>, bool> BuildControlTargets()
    {
        var incomingControl = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var portalSources = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var edge in _controlEdgesByFrom.SelectMany(g => g))
            if (_eventNodesById.ContainsKey(edge.FromId))
            {
                if (_eventNodesById.ContainsKey(edge.ToId))
                    Add(incomingControl, edge.ToId, edge.FromId);
                else
                    Add(portalSources, edge.ToId, edge.FromId);
            }
            else if (portalSources.TryGetValue(edge.FromId, out var sources) &&
                     _eventNodesById.ContainsKey(edge.ToId))
            {
                foreach (var source in sources)
                    Add(incomingControl, edge.ToId, source);
            }

        return (eventId, reachable) =>
            incomingControl.TryGetValue(eventId, out var sources) && sources.Any(reachable.Contains);

        static void Add(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = [];
            list.Add(value);
        }
    }
}