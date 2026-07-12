// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.Globalization;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Sim;

/// <summary>An immutable simulation snapshot; every command returns a new one.</summary>
public sealed record StorySimSnapshot
{
    public required StoryRuntimeState Runtime { get; init; }
    public double Clock { get; init; }

    /// <summary>Node ids whose trigger has been satisfied by a control edge (TRIGGER_EVENT etc.).</summary>
    public ImmutableHashSet<string> Triggered { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    /// <summary>Human-readable step log, newest last.</summary>
    public ImmutableList<string> Log { get; init; } = ImmutableList<string>.Empty;
}

/// <summary>One actionable choice the simulation is waiting on.</summary>
public sealed record StorySimIntervention(
    string Kind, // "manual" | "lua" | "tactical"
    string NodeId,
    string EventName,
    string? EventType,
    IReadOnlyList<string> Options);

/// <summary>
///     Semantic story simulation over <see cref="StoryEvaluator" /> — no game process, no DAP.
///     Deterministic by construction: state transitions are pure, cascades fire events in graph
///     node order, and every event auto-fires at most once per cascade (perpetual events re-arm
///     and may fire again on the NEXT command).
///     Modelled semantics: <c>STORY_ELAPSED</c> fires from the virtual clock; control edges
///     (schema <c>StoryEventName</c> reward params — TRIGGER_EVENT and friends) satisfy the
///     target's trigger, or disable it when the edge label carries <c>DISABLE</c>;
///     <c>STORY_FLAGS</c> fires when all its flag params are set; flag-writing rewards set
///     (or, for <c>REMOVE_*</c>, clear) their flags; <c>STORY_ELEMENT</c> rewards activate the
///     named suspended thread. Everything else is a manual intervention: SatisfyTrigger,
///     LuaNotify for <c>STORY_AI_NOTIFICATION</c>, and tactical outcomes resolved by firing the
///     armed <c>STORY_VICTORY</c>/<c>STORY_MISSION_LOST</c> events.
/// </summary>
public sealed class StorySimulator
{
    private readonly StoryEvaluator _evaluator;
    private readonly Dictionary<(string Type, int Position), string> _eventParamTypes;
    private readonly Dictionary<(string Type, int Position), string> _rewardParamTypes;
    private readonly StoryCampaignModel _model;
    private readonly IReadOnlyList<StoryNode> _eventNodes;
    private readonly ILookup<string, StoryEdge> _controlEdgesByFrom;

    public StorySimulator(StoryCampaignModel model, ISchemaProvider schema)
    {
        _model = model;
        _evaluator = new StoryEvaluator(model.Graph);
        _eventNodes = model.Graph.Nodes.Where(n => n.Kind == StoryNodeKind.Event).ToList();
        _controlEdgesByFrom = model.Graph.Edges
            .Where(e => e.Kind == StoryEdgeKind.Control)
            .ToLookup(e => e.FromId, StringComparer.Ordinal);
        _eventParamTypes = StoryReferenceTypes.BuildParamMap(schema.GetEnum("StoryEventType"));
        _rewardParamTypes = StoryReferenceTypes.BuildParamMap(schema.GetEnum("StoryRewardType"));
    }

    public StorySimSnapshot Start()
    {
        var snapshot = new StorySimSnapshot
        {
            Runtime = StoryRuntimeState.Initial with
            {
                SuspendedThreads = StoryRuntimeState.Initial.SuspendedThreads
                    .Union(_model.SuspendedThreadUris)
            },
            Log = ImmutableList.Create("Simulation started.")
        };
        return Cascade(snapshot);
    }

    /// <summary>Manually fires an armed event (the user says its trigger condition happened).</summary>
    public StorySimSnapshot SatisfyTrigger(StorySimSnapshot snapshot, string nodeId)
    {
        var node = _eventNodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null)
            return snapshot with { Log = snapshot.Log.Add($"⚠ Unknown event node '{nodeId}'.") };
        if (_evaluator.GetLifecycle(nodeId, snapshot.Runtime) != StoryEventLifecycle.Armed)
            return snapshot with
            {
                Log = snapshot.Log.Add($"⚠ '{node.Event!.Name}' is not armed — trigger ignored.")
            };

        return Cascade(Fire(snapshot, node, "manual trigger"));
    }

    public StorySimSnapshot SetFlag(StorySimSnapshot snapshot, string flag, int value)
    {
        var next = snapshot with
        {
            Runtime = snapshot.Runtime.WithFlag(flag, value),
            Log = snapshot.Log.Add($"Flag {flag} = {value}.")
        };
        return Cascade(next);
    }

    public StorySimSnapshot AdvanceClock(StorySimSnapshot snapshot, double seconds)
    {
        if (seconds <= 0) return snapshot;
        var next = snapshot with
        {
            Clock = snapshot.Clock + seconds,
            Log = snapshot.Log.Add($"Clock advanced to {snapshot.Clock + seconds:0.##}s.")
        };
        return Cascade(next);
    }

    /// <summary>Simulates Lua calling <c>Story_Event("id")</c>: fires armed AI-notification events with that id.</summary>
    public StorySimSnapshot LuaNotify(StorySimSnapshot snapshot, string notificationId)
    {
        var next = snapshot with { Log = snapshot.Log.Add($"Lua Story_Event(\"{notificationId}\").") };
        var fired = false;
        foreach (var node in _eventNodes)
        {
            if (!string.Equals(node.Event!.EventType, "STORY_AI_NOTIFICATION", StringComparison.OrdinalIgnoreCase))
                continue;
            if (_evaluator.GetLifecycle(node.Id, next.Runtime) != StoryEventLifecycle.Armed) continue;
            if (!NotificationIdsOf(node.Event).Any(id =>
                    string.Equals(id, notificationId, StringComparison.OrdinalIgnoreCase))) continue;
            next = Fire(next, node, "Lua notification");
            fired = true;
        }

        if (!fired)
            next = next with { Log = next.Log.Add($"⚠ No armed event listens for '{notificationId}'.") };
        return Cascade(next);
    }

    // ── Read model ───────────────────────────────────────────────────────────

    public IReadOnlyDictionary<string, StoryEventLifecycle> GetLifecycles(StorySimSnapshot snapshot)
    {
        return _eventNodes.ToDictionary(n => n.Id, n => _evaluator.GetLifecycle(n.Id, snapshot.Runtime),
            StringComparer.Ordinal);
    }

    /// <summary>What the simulation is waiting on: armed events whose trigger the model cannot fire itself.</summary>
    public IReadOnlyList<StorySimIntervention> GetInterventions(StorySimSnapshot snapshot)
    {
        var interventions = new List<StorySimIntervention>();
        foreach (var node in _eventNodes)
        {
            if (_evaluator.GetLifecycle(node.Id, snapshot.Runtime) != StoryEventLifecycle.Armed) continue;
            var storyEvent = node.Event!;
            var type = storyEvent.EventType?.ToUpperInvariant();
            if (type is "STORY_ELAPSED" or "STORY_TRIGGER" or "STORY_FLAGS") continue; // auto-firing

            var kind = type switch
            {
                "STORY_AI_NOTIFICATION" => "lua",
                "STORY_VICTORY" or "STORY_MISSION_LOST" => "tactical",
                _ => "manual"
            };
            var options = kind == "lua" ? NotificationIdsOf(storyEvent).ToList() : [];
            interventions.Add(new StorySimIntervention(kind, node.Id, storyEvent.Name,
                storyEvent.EventType, options));
        }

        return interventions;
    }

    // ── Step semantics ───────────────────────────────────────────────────────

    /// <summary>Fires all auto-firable armed events to a fixpoint; each node at most once per cascade.</summary>
    private StorySimSnapshot Cascade(StorySimSnapshot snapshot)
    {
        var firedThisCascade = new HashSet<string>(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var node in _eventNodes)
            {
                if (firedThisCascade.Contains(node.Id)) continue;
                if (_evaluator.GetLifecycle(node.Id, snapshot.Runtime) != StoryEventLifecycle.Armed) continue;
                if (!AutoTriggerSatisfied(node, snapshot)) continue;

                snapshot = Fire(snapshot, node, "auto");
                firedThisCascade.Add(node.Id);
                changed = true;
            }
        } while (changed);

        return snapshot;
    }

    private bool AutoTriggerSatisfied(StoryNode node, StorySimSnapshot snapshot)
    {
        // A satisfied control edge (TRIGGER_EVENT and friends) force-fires any armed event.
        if (snapshot.Triggered.Contains(node.Id)) return true;

        var storyEvent = node.Event!;
        switch (storyEvent.EventType?.ToUpperInvariant())
        {
            case "STORY_ELAPSED":
            {
                var raw = storyEvent.EventParams.FirstOrDefault(p => p.Position == 0)?.RawValue;
                return raw is not null
                       && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var at)
                       && snapshot.Clock >= at;
            }
            case "STORY_FLAGS":
            {
                var flags = FlagsReadBy(storyEvent).ToList();
                return flags.Count > 0 && flags.All(f => snapshot.Runtime.Flags.GetValueOrDefault(f) != 0);
            }
            default:
                return false;
        }
    }

    private StorySimSnapshot Fire(StorySimSnapshot snapshot, StoryNode node, string reason)
    {
        var storyEvent = node.Event!;
        var runtime = snapshot.Runtime.WithFired(node.Id);
        var triggered = snapshot.Triggered.Remove(node.Id);
        var log = snapshot.Log.Add($"Fired '{storyEvent.Name}' ({reason}).");

        // Control edges: DISABLE_* rewards disable the target, everything else satisfies its trigger.
        foreach (var edge in _controlEdgesByFrom[node.Id].SelectMany(ExpandThroughPortals))
        {
            if (edge.Label?.Contains("DISABLE", StringComparison.OrdinalIgnoreCase) == true)
            {
                runtime = runtime.WithDisabled(edge.ToId);
                log = log.Add($"  → disabled '{EventNameOf(edge.ToId)}'.");
            }
            else
            {
                triggered = triggered.Add(edge.ToId);
                log = log.Add($"  → triggered '{EventNameOf(edge.ToId)}'.");
            }
        }

        // Flag-writing rewards (schema StoryFlag params on the reward side).
        if (storyEvent.RewardType is { } rewardType)
        {
            var clears = rewardType.Contains("REMOVE", StringComparison.OrdinalIgnoreCase);
            foreach (var slot in storyEvent.RewardParams)
            {
                if (_rewardParamTypes.GetValueOrDefault((rewardType.ToUpperInvariant(), slot.Position))
                    != StoryReferenceTypes.Flag) continue;
                foreach (var flag in StoryReferenceTypes.SplitList(slot.RawValue))
                {
                    runtime = runtime.WithFlag(flag, clears ? 0 : 1);
                    log = log.Add($"  → flag {flag} = {(clears ? 0 : 1)}.");
                }
            }

            // STORY_ELEMENT activates a suspended thread (param = thread name sans .xml).
            if (rewardType.Equals("STORY_ELEMENT", StringComparison.OrdinalIgnoreCase))
            {
                var element = storyEvent.RewardParams.FirstOrDefault(p => p.Position == 0)?.RawValue;
                if (!string.IsNullOrEmpty(element))
                {
                    var suffix = "/" + element.ToLowerInvariant() + ".xml";
                    var match = runtime.SuspendedThreads.FirstOrDefault(u =>
                        u.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                    {
                        runtime = runtime with { SuspendedThreads = runtime.SuspendedThreads.Remove(match) };
                        log = log.Add($"  → activated thread '{element}'.");
                    }
                }
            }
        }

        return snapshot with { Runtime = runtime, Triggered = triggered, Log = log };
    }

    // A control edge into a portal continues to the portal's outgoing control edges.
    private IEnumerable<StoryEdge> ExpandThroughPortals(StoryEdge edge)
    {
        if (_eventNodes.Any(n => n.Id == edge.ToId))
        {
            yield return edge;
            yield break;
        }

        foreach (var hop in _controlEdgesByFrom[edge.ToId])
            yield return hop;
    }

    private IEnumerable<string> FlagsReadBy(StoryEvent storyEvent)
    {
        var type = storyEvent.EventType?.ToUpperInvariant();
        if (type is null) yield break;
        foreach (var slot in storyEvent.EventParams)
        {
            if (_eventParamTypes.GetValueOrDefault((type, slot.Position)) != StoryReferenceTypes.Flag)
                continue;
            foreach (var flag in StoryReferenceTypes.SplitList(slot.RawValue))
                yield return flag;
        }
    }

    private IEnumerable<string> NotificationIdsOf(StoryEvent storyEvent)
    {
        var type = storyEvent.EventType?.ToUpperInvariant();
        if (type is null) yield break;
        foreach (var slot in storyEvent.EventParams)
            if (_eventParamTypes.GetValueOrDefault((type, slot.Position)) == StoryReferenceTypes.Notification)
                yield return slot.RawValue;
    }

    private string EventNameOf(string nodeId)
    {
        return _eventNodes.FirstOrDefault(n => n.Id == nodeId)?.Event!.Name ?? nodeId;
    }
}
