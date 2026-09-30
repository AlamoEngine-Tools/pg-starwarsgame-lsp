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

    /// <summary>
    ///     Armed listeners the author ruled out for this run: they stay armed, as in the game, but
    ///     are no decision until reconsidered. Firing one after all clears it.
    /// </summary>
    public ImmutableHashSet<string> RuledOut { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    public ImmutableHashSet<string> SuspendedThreads { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    public ImmutableDictionary<string, int> Flags { get; init; } =
        ImmutableDictionary.Create<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     The engine's per-event Active flag, tracked explicitly. Null means "not tracked": static
    ///     analysis derives arming from the prereqs instead. The simulator tracks it because the
    ///     engine arms by PUSH (a firing prereq re-evaluates its dependants) and a reset drops the
    ///     flag until the next push - which a derived view cannot express.
    /// </summary>
    public ImmutableHashSet<string>? ArmedEvents { get; init; }

    /// <summary>Virtual clock at which each event was last armed; STORY_ELAPSED counts from here.</summary>
    public ImmutableDictionary<string, double> ArmedAt { get; init; } =
        ImmutableDictionary.Create<string, double>(StringComparer.Ordinal);

    /// <summary>
    ///     Game-side completions the simulator owes: a SPEECH or START_MOVIE reward was given and
    ///     the matching STORY_SPEECH_DONE / STORY_MOVIE_DONE dispatch happens on the next command.
    ///     Keyed "EVENT_TYPE|name" (upper-cased name, the engine compares upper-cased).
    /// </summary>
    public ImmutableHashSet<string> PendingCompletions { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The fact table the simulator's world changes and rewards write; empty for static analysis.</summary>
    public Sim.StoryWorld World { get; init; } = Sim.StoryWorld.Empty;

    /// <summary>
    ///     Per-event dispatch counts for the counting types (a STORY_CONQUER with a count, the
    ///     battle counters): the engine keeps these on the event object and resets them with it.
    /// </summary>
    public ImmutableDictionary<string, int> Hits { get; init; } =
        ImmutableDictionary.Create<string, int>(StringComparer.Ordinal);

    public StoryRuntimeState WithWorld(Sim.StoryWorld world)
    {
        return this with { World = world };
    }

    public StoryRuntimeState WithHit(string eventNodeId)
    {
        return this with { Hits = Hits.SetItem(eventNodeId, Hits.GetValueOrDefault(eventNodeId) + 1) };
    }

    public StoryRuntimeState WithoutHits(string eventNodeId)
    {
        return this with { Hits = Hits.Remove(eventNodeId) };
    }

    /// <summary>Each campaign script's PGStateMachine, keyed by script uri.</summary>
    public ImmutableDictionary<string, Sim.LuaScriptState> Scripts { get; init; } =
        ImmutableDictionary.Create<string, Sim.LuaScriptState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Story_Event calls a script's threads will make once their sleeps are over.</summary>
    public ImmutableList<Sim.LuaPendingEmission> PendingEmissions { get; init; } =
        ImmutableList<Sim.LuaPendingEmission>.Empty;

    public StoryRuntimeState WithScript(string scriptUri, Sim.LuaScriptState state)
    {
        return this with { Scripts = Scripts.SetItem(scriptUri, state) };
    }

    public StoryRuntimeState WithFired(string eventNodeId)
    {
        return this with { FiredEvents = FiredEvents.Add(eventNodeId) };
    }

    public StoryRuntimeState WithUnfired(string eventNodeId)
    {
        return this with { FiredEvents = FiredEvents.Remove(eventNodeId) };
    }

    public StoryRuntimeState WithDisabled(string eventNodeId)
    {
        return this with { DisabledEvents = DisabledEvents.Add(eventNodeId) };
    }

    public StoryRuntimeState WithEnabled(string eventNodeId)
    {
        return this with { DisabledEvents = DisabledEvents.Remove(eventNodeId) };
    }

    public StoryRuntimeState WithArmed(string eventNodeId, double clock)
    {
        var armed = ArmedEvents ?? ImmutableHashSet.Create<string>(StringComparer.Ordinal);
        return this with { ArmedEvents = armed.Add(eventNodeId), ArmedAt = ArmedAt.SetItem(eventNodeId, clock) };
    }

    public StoryRuntimeState WithUnarmed(string eventNodeId)
    {
        return ArmedEvents is null
            ? this
            : this with { ArmedEvents = ArmedEvents.Remove(eventNodeId), ArmedAt = ArmedAt.Remove(eventNodeId) };
    }

    public StoryRuntimeState WithCompletionPending(string key)
    {
        return this with { PendingCompletions = PendingCompletions.Add(key) };
    }

    public StoryRuntimeState WithoutCompletions(IEnumerable<string> keys)
    {
        return this with { PendingCompletions = PendingCompletions.Except(keys) };
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

        // Tracked arming (the simulator) is the engine's Active flag; the static view derives it.
        var armed = state.ArmedEvents?.Contains(eventNodeId) ?? PrereqsSatisfied(node.Event!, state);
        return armed ? StoryEventLifecycle.Armed : StoryEventLifecycle.Waiting;
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