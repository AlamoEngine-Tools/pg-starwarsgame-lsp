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
///     Semantic story simulation over <see cref="StoryEvaluator" /> - no game process, no DAP.
///     Deterministic by construction: state transitions are pure, cascades poll events in graph
///     node order, and every polled event fires at most once per cascade (perpetual events re-arm
///     and may fire again on the NEXT command).
///     The event machine reproduces the engine as measured in the decompile (2026-09-19):
///     an event is ARMED (the engine's Active flag) at load when it has no prereq line, or by a
///     PUSH when a prereq fires and one of its lines is fully fired; a <c>STORY_TRIGGER</c> fires
///     inside that push. Firing runs the reward, pushes the dependants, and re-arms a perpetual
///     event (except a perpetual <c>STORY_TRIGGER</c>, which waits for the next push).
///     <c>TRIGGER_EVENT</c> fires its target in every thread with no prereq, armed or fired
///     check; <c>RESET_EVENT</c> clears the fired flag and drops arming until the next push;
///     <c>RESET_BRANCH</c> clears every member and re-pushes them; the DISABLE rewards need their
///     boolean or are ignored. A disabled event still arms but its fire is swallowed. Polled
///     types: <c>STORY_ELAPSED</c> counts from arming; <c>STORY_FLAG</c> defaults to EQUAL_TO 0
///     and never fires on an unset flag. SPEECH and START_MOVIE rewards owe a completion that
///     the next command dispatches. Everything else is a manual intervention: SatisfyTrigger,
///     LuaNotify for <c>STORY_AI_NOTIFICATION</c>, and tactical outcomes resolved by firing the
///     armed <c>STORY_VICTORY</c>/<c>STORY_MISSION_LOST</c>/<c>STORY_MISSION_FAILED</c> events.
/// </summary>
public sealed class StorySimulator
{
    private const int MaxFireDepth = 64;
    private const string SpeechDone = "STORY_SPEECH_DONE";
    private const string MovieDone = "STORY_MOVIE_DONE";

    private readonly Dictionary<string, List<StoryNode>> _dependantsById;
    private readonly Dictionary<(string Type, int Position), string> _eventParamTypes;
    private readonly IReadOnlyList<StoryNode> _eventNodes;
    private readonly Dictionary<string, List<StoryNode>> _eventsByName;
    private readonly StoryEvaluator _evaluator;
    private readonly StoryCampaignModel _model;
    private readonly Dictionary<string, StoryNode> _nodesById;
    private readonly Dictionary<string, List<List<string>>> _prereqLinesById;
    private readonly Dictionary<(string Type, int Position), string> _rewardParamTypes;
    private readonly List<string> _startupNotes = [];

    public StorySimulator(StoryCampaignModel model, ISchemaProvider schema)
    {
        _model = model;
        _evaluator = new StoryEvaluator(model.Graph);
        _eventNodes = model.Graph.Nodes.Where(n => n.Kind == StoryNodeKind.Event).ToList();
        _nodesById = _eventNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        _eventsByName = new Dictionary<string, List<StoryNode>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in _eventNodes)
        {
            if (!_eventsByName.TryGetValue(node.Event!.Name, out var list))
                _eventsByName[node.Event.Name] = list = [];
            list.Add(node);
        }

        _eventParamTypes = StoryReferenceTypes.BuildParamMap(schema.GetEnum("StoryEventType"));
        _rewardParamTypes = StoryReferenceTypes.BuildParamMap(schema.GetEnum("StoryRewardType"));

        // Prereqs resolve inside the event's own thread (Compute_Dependants asks the subplot);
        // a token the thread does not hold is dropped from its line, which is what the engine's
        // release build does after the assert.
        _prereqLinesById = new Dictionary<string, List<List<string>>>(StringComparer.Ordinal);
        _dependantsById = new Dictionary<string, List<StoryNode>>(StringComparer.Ordinal);
        foreach (var node in _eventNodes)
        {
            var lines = new List<List<string>>();
            foreach (var group in node.Event!.PrereqGroups)
            {
                var line = new List<string>();
                foreach (var token in group.Tokens)
                {
                    var prereqId = node.ThreadUri is null
                        ? null
                        : StoryGraphBuilder.EventNodeId(node.ThreadUri, token.Text);
                    if (prereqId is not null && _nodesById.ContainsKey(prereqId))
                    {
                        line.Add(prereqId);
                        if (!_dependantsById.TryGetValue(prereqId, out var dependants))
                            _dependantsById[prereqId] = dependants = [];
                        if (dependants.All(d => d.Id != node.Id)) dependants.Add(node);
                    }
                    else
                    {
                        _startupNotes.Add(
                            $"Prereq '{token.Text}' of '{node.Event.Name}' is not in its thread - the engine drops it from the line.");
                    }
                }

                lines.Add(line);
            }

            _prereqLinesById[node.Id] = lines;
        }
    }

    public StorySimSnapshot Start()
    {
        // The parser arms every event that has no prereq line; everything else waits for a push.
        var runtime = StoryRuntimeState.Initial with
        {
            SuspendedThreads = StoryRuntimeState.Initial.SuspendedThreads.Union(_model.SuspendedThreadUris),
            ArmedEvents = ImmutableHashSet.Create<string>(StringComparer.Ordinal)
        };
        foreach (var node in _eventNodes)
            if (node.Event!.PrereqGroups.Count == 0)
                runtime = runtime.WithArmed(node.Id, 0);

        var snapshot = new StorySimSnapshot
        {
            Runtime = runtime,
            Log = ImmutableList.Create("Simulation started.").AddRange(_startupNotes)
        };
        return Cascade(snapshot);
    }

    /// <summary>Manually fires an active event (the user says its trigger condition happened).</summary>
    public StorySimSnapshot SatisfyTrigger(StorySimSnapshot snapshot, string nodeId)
    {
        snapshot = DispatchCompletions(snapshot);
        if (!_nodesById.TryGetValue(nodeId, out var node))
            return snapshot with { Log = snapshot.Log.Add($"Unknown event node '{nodeId}'.") };
        if (!IsActive(node, snapshot.Runtime))
            return snapshot with
            {
                Log = snapshot.Log.Add($"'{node.Event!.Name}' is not armed - trigger ignored.")
            };

        return Cascade(Fire(snapshot, node, "manual trigger", 0));
    }

    public StorySimSnapshot SetFlag(StorySimSnapshot snapshot, string flag, int value)
    {
        snapshot = DispatchCompletions(snapshot);
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
        snapshot = DispatchCompletions(snapshot);
        var next = snapshot with
        {
            Clock = snapshot.Clock + seconds,
            Log = snapshot.Log.Add($"Clock advanced to {snapshot.Clock + seconds:0.##}s.")
        };
        return Cascade(next);
    }

    /// <summary>Simulates Lua calling <c>Story_Event("id")</c>: fires active AI-notification events with that id.</summary>
    public StorySimSnapshot LuaNotify(StorySimSnapshot snapshot, string notificationId)
    {
        snapshot = DispatchCompletions(snapshot);
        var next = snapshot with { Log = snapshot.Log.Add($"Lua Story_Event(\"{notificationId}\").") };
        var fired = false;
        foreach (var node in _eventNodes)
        {
            if (!string.Equals(node.Event!.EventType, "STORY_AI_NOTIFICATION", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsActive(node, next.Runtime)) continue;
            if (!NotificationIdsOf(node.Event).Any(id =>
                    string.Equals(id, notificationId, StringComparison.OrdinalIgnoreCase))) continue;
            next = Fire(next, node, "Lua notification", 0);
            fired = true;
        }

        if (!fired)
            next = next with { Log = next.Log.Add($"No armed event listens for '{notificationId}'.") };
        return Cascade(next);
    }

    // ── Read model ───────────────────────────────────────────────────────────

    public IReadOnlyDictionary<string, StoryEventLifecycle> GetLifecycles(StorySimSnapshot snapshot)
    {
        return _eventNodes.ToDictionary(n => n.Id, n => _evaluator.GetLifecycle(n.Id, snapshot.Runtime),
            StringComparer.Ordinal);
    }

    /// <summary>What the simulation is waiting on: active events whose trigger the model cannot fire itself.</summary>
    public IReadOnlyList<StorySimIntervention> GetInterventions(StorySimSnapshot snapshot)
    {
        var interventions = new List<StorySimIntervention>();
        foreach (var node in _eventNodes)
        {
            if (!IsActive(node, snapshot.Runtime)) continue;
            var storyEvent = node.Event!;
            var type = storyEvent.EventType?.ToUpperInvariant();
            // Polled types fire on their own; a STORY_TRIGGER only ever fires from a push.
            if (type is "STORY_ELAPSED" or "STORY_TRIGGER" or "STORY_FLAG") continue;
            // A completion the simulator already owes will dispatch on the next command.
            if (type is SpeechDone or MovieDone && HasPendingCompletion(node, snapshot.Runtime)) continue;

            var kind = type switch
            {
                "STORY_AI_NOTIFICATION" => "lua",
                "STORY_VICTORY" or "STORY_MISSION_LOST" or "STORY_MISSION_FAILED" => "tactical",
                _ => "manual"
            };
            var options = kind == "lua" ? NotificationIdsOf(storyEvent).ToList() : [];
            interventions.Add(new StorySimIntervention(kind, node.Id, storyEvent.Name,
                storyEvent.EventType, options));
        }

        return interventions;
    }

    // ── Step semantics ───────────────────────────────────────────────────────

    /// <summary>
    ///     One command's worth of engine frames: poll the clock and flag events to a fixpoint.
    ///     Each polled node fires at most once per cascade; pushes (prereqs, TRIGGER_EVENT) run
    ///     inside Fire. Completions owed by the previous command are dispatched by each command
    ///     before it acts, so a reward given now completes on the NEXT command.
    /// </summary>
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
                if (!IsActive(node, snapshot.Runtime)) continue;
                if (!PolledTriggerSatisfied(node, snapshot)) continue;

                snapshot = Fire(snapshot, node, "auto", 0);
                firedThisCascade.Add(node.Id);
                changed = true;
            }
        } while (changed);

        return snapshot;
    }

    private StorySimSnapshot DispatchCompletions(StorySimSnapshot snapshot)
    {
        var pending = snapshot.Runtime.PendingCompletions;
        if (pending.Count == 0) return snapshot;

        // Consume first: the engine dispatches once and an event not active at that moment misses it.
        snapshot = snapshot with { Runtime = snapshot.Runtime.WithoutCompletions(pending) };
        foreach (var node in _eventNodes)
        {
            if (!IsActive(node, snapshot.Runtime)) continue;
            var type = node.Event!.EventType?.ToUpperInvariant();
            if (type is not (SpeechDone or MovieDone)) continue;
            var match = StringTokensOf(node.Event).FirstOrDefault(t => pending.Contains(type + "|" + t));
            if (match is null) continue;
            snapshot = Fire(snapshot, node, type == SpeechDone ? $"speech '{match}' done" : $"movie '{match}' done", 0);
        }

        return snapshot;
    }

    /// <summary>Engine Is_Event_Active: armed, not fired, not disabled - and the thread must be running.</summary>
    private static bool IsActive(StoryNode node, StoryRuntimeState runtime)
    {
        if (runtime.DisabledEvents.Contains(node.Id)) return false;
        if (runtime.FiredEvents.Contains(node.Id)) return false;
        if (node.ThreadUri is not null && runtime.SuspendedThreads.Contains(node.ThreadUri)) return false;
        return runtime.ArmedEvents?.Contains(node.Id) == true;
    }

    private bool PolledTriggerSatisfied(StoryNode node, StorySimSnapshot snapshot)
    {
        var storyEvent = node.Event!;
        switch (storyEvent.EventType?.ToUpperInvariant())
        {
            case "STORY_ELAPSED":
            {
                // Measured: elapsed accumulates from the first evaluation after arming.
                var raw = storyEvent.EventParams.FirstOrDefault(p => p.Position == 0)?.RawValue;
                if (raw is null ||
                    !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var at))
                    return false;
                var armedAt = snapshot.Runtime.ArmedAt.GetValueOrDefault(node.Id);
                return snapshot.Clock - armedAt >= at;
            }
            case "STORY_FLAG":
            {
                // Measured: OR over the flag list; an unset flag never compares; the constructor
                // default is EQUAL_TO 0; an operator name the engine does not know falls to
                // COMPARE_NONE, which the switch treats as GREATER_THAN. NOT_EQUAL_TO is in the
                // schema but has no case in the engine's switch, so it never fires (inferred).
                var flags = FlagsReadBy(storyEvent).ToList();
                if (flags.Count == 0) return false;
                var rawTarget = storyEvent.EventParams.FirstOrDefault(p => p.Position == 1)?.RawValue;
                var target = int.TryParse(rawTarget, out var parsed) ? parsed : 0;
                var op = storyEvent.EventParams.FirstOrDefault(p => p.Position == 2)?.RawValue.Trim()
                    .ToUpperInvariant();
                return flags.Any(f =>
                {
                    if (!snapshot.Runtime.Flags.TryGetValue(f, out var value)) return false;
                    return op switch
                    {
                        null or "" or "EQUAL_TO" => value == target,
                        "LESS_THAN" => value < target,
                        "GREATER_THAN_EQUAL_TO" => value >= target,
                        "LESS_THAN_EQUAL_TO" => value <= target,
                        "NOT_EQUAL_TO" => false,
                        _ => value > target
                    };
                });
            }
            default:
                return false;
        }
    }

    /// <summary>Engine Event_Triggered: swallowed when disabled, else reward, push dependants, perpetual re-arm.</summary>
    private StorySimSnapshot Fire(StorySimSnapshot snapshot, StoryNode node, string reason, int depth)
    {
        var storyEvent = node.Event!;
        if (depth > MaxFireDepth)
            return snapshot with
            {
                Log = snapshot.Log.Add($"'{storyEvent.Name}' not fired - trigger chain deeper than {MaxFireDepth}.")
            };
        if (snapshot.Runtime.DisabledEvents.Contains(node.Id))
            return snapshot with
            {
                Log = snapshot.Log.Add($"'{storyEvent.Name}' is disabled - fire swallowed ({reason}).")
            };

        snapshot = snapshot with
        {
            Runtime = snapshot.Runtime.WithFired(node.Id),
            Log = snapshot.Log.Add($"Fired '{storyEvent.Name}' ({reason}).")
        };

        snapshot = GiveReward(snapshot, node, depth);

        if (_dependantsById.TryGetValue(node.Id, out var dependants))
            foreach (var dependant in dependants)
                snapshot = ParentTriggered(snapshot, dependant, depth + 1);

        if (!storyEvent.Perpetual) return snapshot;

        // Perpetual: Triggered cleared, Reset (drops arming when it has prereqs), then Active
        // again for every type but STORY_TRIGGER, which waits for the next push.
        var runtime = snapshot.Runtime.WithUnfired(node.Id);
        if (storyEvent.PrereqGroups.Count > 0) runtime = runtime.WithUnarmed(node.Id);
        if (!IsStoryTrigger(storyEvent)) runtime = runtime.WithArmed(node.Id, snapshot.Clock);
        return snapshot with { Runtime = runtime };
    }

    /// <summary>Engine Parent_Triggered: arm when a line is fully fired; a STORY_TRIGGER fires at once.</summary>
    private StorySimSnapshot ParentTriggered(StorySimSnapshot snapshot, StoryNode node, int depth)
    {
        var runtime = snapshot.Runtime;
        if (runtime.ArmedEvents?.Contains(node.Id) == true || runtime.FiredEvents.Contains(node.Id))
            return snapshot;

        var lines = _prereqLinesById[node.Id];
        if (!lines.Any(line => line.All(runtime.FiredEvents.Contains))) return snapshot;

        snapshot = snapshot with
        {
            Runtime = runtime.WithArmed(node.Id, snapshot.Clock),
            Log = snapshot.Log.Add($"  -> armed '{node.Event!.Name}'.")
        };
        return IsStoryTrigger(node.Event) ? Fire(snapshot, node, "prereqs met", depth) : snapshot;
    }

    private StorySimSnapshot GiveReward(StorySimSnapshot snapshot, StoryNode node, int depth)
    {
        var storyEvent = node.Event!;
        if (storyEvent.RewardType is not { } rewardType) return snapshot;
        var upperReward = rewardType.ToUpperInvariant();

        snapshot = ApplyFlagRewards(snapshot, storyEvent, upperReward);

        switch (upperReward)
        {
            case "STORY_ELEMENT":
                return ActivateThread(snapshot, storyEvent);
            case "TRIGGER_EVENT":
                return RewardTriggerEvent(snapshot, storyEvent, depth);
            case "RESET_EVENT":
                return RewardResetEvent(snapshot, node);
            case "RESET_BRANCH":
                return RewardResetBranch(snapshot, node, depth);
            case "DISABLE_STORY_EVENT":
                return RewardDisableStoryEvent(snapshot, node);
            case "DISABLE_BRANCH":
                return RewardDisableBranch(snapshot, node);
            case "SPEECH":
                return OweCompletion(snapshot, storyEvent, SpeechDone);
            case "START_MOVIE":
                return OweCompletion(snapshot, storyEvent, MovieDone);
            default:
                return snapshot;
        }
    }

    private StorySimSnapshot ApplyFlagRewards(StorySimSnapshot snapshot, StoryEvent storyEvent, string upperReward)
    {
        // Flag-writing rewards (schema StoryFlag params on the reward side). SET_FLAG writes
        // param 1 (default 1), INCREMENT_FLAG adds param 1 (default 1, may be negative), other
        // flag-writing rewards conservatively set 1.
        var rawAmount = storyEvent.RewardParams.FirstOrDefault(p => p.Position == 1)?.RawValue;
        var amount = int.TryParse(rawAmount, out var parsedAmount) ? parsedAmount : 1;
        var runtime = snapshot.Runtime;
        var log = snapshot.Log;
        foreach (var slot in storyEvent.RewardParams)
        {
            if (_rewardParamTypes.GetValueOrDefault((upperReward, slot.Position)) != StoryReferenceTypes.Flag)
                continue;
            foreach (var flag in StoryReferenceTypes.SplitList(slot.RawValue))
            {
                var value = upperReward == "INCREMENT_FLAG"
                    ? runtime.Flags.GetValueOrDefault(flag) + amount
                    : upperReward == "SET_FLAG"
                        ? amount
                        : 1;
                runtime = runtime.WithFlag(flag, value);
                log = log.Add($"  -> flag {flag} = {value}.");
            }
        }

        return snapshot with { Runtime = runtime, Log = log };
    }

    // STORY_ELEMENT activates a suspended thread (param = thread name sans .xml).
    private static StorySimSnapshot ActivateThread(StorySimSnapshot snapshot, StoryEvent storyEvent)
    {
        var element = storyEvent.RewardParams.FirstOrDefault(p => p.Position == 0)?.RawValue;
        if (string.IsNullOrEmpty(element)) return snapshot;
        var suffix = "/" + element.ToLowerInvariant() + ".xml";
        var match = snapshot.Runtime.SuspendedThreads.FirstOrDefault(u =>
            u.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (match is null) return snapshot;
        return snapshot with
        {
            Runtime = snapshot.Runtime with { SuspendedThreads = snapshot.Runtime.SuspendedThreads.Remove(match) },
            Log = snapshot.Log.Add($"  -> activated thread '{element}'.")
        };
    }

    // Measured: Trigger_Event(name) runs Event_Triggered on the named event in EVERY subplot,
    // with no prereq, armed or fired check - a waiting event fires, a fired one fires again.
    private StorySimSnapshot RewardTriggerEvent(StorySimSnapshot snapshot, StoryEvent storyEvent, int depth)
    {
        var name = Param(storyEvent, 0);
        if (name is null)
            return snapshot with { Log = snapshot.Log.Add("  -> TRIGGER_EVENT ignored - missing parameter.") };
        if (!_eventsByName.TryGetValue(name, out var targets))
            return snapshot with { Log = snapshot.Log.Add($"  -> TRIGGER_EVENT: no event named '{name}'.") };
        foreach (var target in targets)
        {
            snapshot = snapshot with { Log = snapshot.Log.Add($"  -> triggered '{target.Event!.Name}'.") };
            snapshot = Fire(snapshot, target, $"TRIGGER_EVENT from '{storyEvent.Name}'", depth + 1);
        }

        return snapshot;
    }

    // Measured: Reset_Event on param 0 and every name in the Reward_Param_List, this thread only.
    private StorySimSnapshot RewardResetEvent(StorySimSnapshot snapshot, StoryNode node)
    {
        var storyEvent = node.Event!;
        var names = new List<string>();
        if (Param(storyEvent, 0) is { } first) names.Add(first);
        var listTag = storyEvent.Tags.FirstOrDefault(t =>
            t.Name.Equals("Reward_Param_List", StringComparison.OrdinalIgnoreCase));
        if (listTag is not null) names.AddRange(StoryReferenceTypes.SplitList(listTag.Value));

        foreach (var name in names)
        {
            var target = SameThreadEvent(node, name);
            if (target is null)
            {
                snapshot = snapshot with
                {
                    Log = snapshot.Log.Add($"  -> RESET_EVENT: no event '{name}' in this thread.")
                };
                continue;
            }

            snapshot = snapshot with
            {
                Runtime = ClearTriggered(snapshot.Runtime, target),
                Log = snapshot.Log.Add($"  -> reset '{target.Event!.Name}'.")
            };
        }

        return snapshot;
    }

    // Measured: pass 1 clears every member of the branch, pass 2 re-pushes each (a STORY_TRIGGER
    // member whose prereqs are still fired fires again), then param 1 triggers a named event in
    // this thread. Both params are required for the reset itself.
    private StorySimSnapshot RewardResetBranch(StorySimSnapshot snapshot, StoryNode node, int depth)
    {
        var storyEvent = node.Event!;
        var branch = Param(storyEvent, 0);
        if (branch is null)
            return snapshot with { Log = snapshot.Log.Add("  -> RESET_BRANCH ignored - missing parameter.") };

        var members = BranchMembers(node, branch);
        var runtime = snapshot.Runtime;
        foreach (var member in members) runtime = ClearTriggered(runtime, member);
        snapshot = snapshot with
        {
            Runtime = runtime,
            Log = snapshot.Log.Add($"  -> reset branch '{branch}' ({members.Count} events).")
        };
        foreach (var member in members) snapshot = ParentTriggered(snapshot, member, depth + 1);

        if (Param(storyEvent, 1) is not { } named) return snapshot;
        var target = SameThreadEvent(node, named);
        if (target is null)
            return snapshot with { Log = snapshot.Log.Add($"  -> RESET_BRANCH: no event '{named}' in this thread.") };
        snapshot = snapshot with { Log = snapshot.Log.Add($"  -> triggered '{target.Event!.Name}'.") };
        return Fire(snapshot, target, $"RESET_BRANCH from '{storyEvent.Name}'", depth + 1);
    }

    // Measured: params 0 and 1 are both required or the reward is ignored; Disabled =
    // atoi(param 1) != 0; a non-zero param 2 disables the name in every thread.
    private StorySimSnapshot RewardDisableStoryEvent(StorySimSnapshot snapshot, StoryNode node)
    {
        var storyEvent = node.Event!;
        var name = Param(storyEvent, 0);
        var flag = Param(storyEvent, 1);
        if (name is null || flag is null)
            return snapshot with
            {
                Log = snapshot.Log.Add("  -> DISABLE_STORY_EVENT ignored - missing parameter.")
            };

        var disable = ParseInt(flag) != 0;
        var everywhere = ParseInt(Param(storyEvent, 2)) != 0;
        var targets = everywhere
            ? _eventsByName.GetValueOrDefault(name) ?? []
            : SameThreadEvent(node, name) is { } single
                ? [single]
                : [];
        if (targets.Count == 0)
            return snapshot with { Log = snapshot.Log.Add($"  -> DISABLE_STORY_EVENT: no event named '{name}'.") };

        var runtime = snapshot.Runtime;
        var log = snapshot.Log;
        foreach (var target in targets)
        {
            runtime = disable ? runtime.WithDisabled(target.Id) : runtime.WithEnabled(target.Id);
            log = log.Add(disable ? $"  -> disabled '{target.Event!.Name}'." : $"  -> enabled '{target.Event!.Name}'.");
        }

        return snapshot with { Runtime = runtime, Log = log };
    }

    // Measured: both params required; Disabled = atoi(param 1) != 0 on every member, this thread only.
    private StorySimSnapshot RewardDisableBranch(StorySimSnapshot snapshot, StoryNode node)
    {
        var storyEvent = node.Event!;
        var branch = Param(storyEvent, 0);
        var flag = Param(storyEvent, 1);
        if (branch is null || flag is null)
            return snapshot with { Log = snapshot.Log.Add("  -> DISABLE_BRANCH ignored - missing parameter.") };

        var disable = ParseInt(flag) != 0;
        var runtime = snapshot.Runtime;
        var members = BranchMembers(node, branch);
        foreach (var member in members)
            runtime = disable ? runtime.WithDisabled(member.Id) : runtime.WithEnabled(member.Id);
        return snapshot with
        {
            Runtime = runtime,
            Log = snapshot.Log.Add(
                $"  -> {(disable ? "disabled" : "enabled")} branch '{branch}' ({members.Count} events).")
        };
    }

    private static StorySimSnapshot OweCompletion(StorySimSnapshot snapshot, StoryEvent storyEvent, string doneType)
    {
        var name = Param(storyEvent, 0);
        if (name is null) return snapshot;
        return snapshot with
        {
            Runtime = snapshot.Runtime.WithCompletionPending(doneType + "|" + name.ToUpperInvariant()),
            Log = snapshot.Log.Add($"  -> {doneType} '{name}' owed on the next command.")
        };
    }

    // Measured: Clear_Triggered drops Triggered and Reset drops Active when the event has prereqs.
    private static StoryRuntimeState ClearTriggered(StoryRuntimeState runtime, StoryNode node)
    {
        runtime = runtime.WithUnfired(node.Id);
        return node.Event!.PrereqGroups.Count > 0 ? runtime.WithUnarmed(node.Id) : runtime;
    }

    // ── Lookups ──────────────────────────────────────────────────────────────

    private StoryNode? SameThreadEvent(StoryNode from, string name)
    {
        if (from.ThreadUri is null) return null;
        return _nodesById.GetValueOrDefault(StoryGraphBuilder.EventNodeId(from.ThreadUri, name));
    }

    // The engine compares branch names with std::string ==, so this is case-sensitive.
    private List<StoryNode> BranchMembers(StoryNode from, string branch)
    {
        return _eventNodes
            .Where(n => string.Equals(n.ThreadUri, from.ThreadUri, StringComparison.Ordinal)
                        && string.Equals(n.Event!.Branch, branch, StringComparison.Ordinal))
            .ToList();
    }

    private static bool HasPendingCompletion(StoryNode node, StoryRuntimeState runtime)
    {
        var type = node.Event!.EventType!.ToUpperInvariant();
        return StringTokensOf(node.Event).Any(t => runtime.PendingCompletions.Contains(type + "|" + t));
    }

    private static bool IsStoryTrigger(StoryEvent storyEvent)
    {
        return string.Equals(storyEvent.EventType, "STORY_TRIGGER", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Param(StoryEvent storyEvent, int position)
    {
        var raw = storyEvent.RewardParams.FirstOrDefault(p => p.Position == position)?.RawValue.Trim();
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    private static int ParseInt(string? raw)
    {
        // atoi: leading integer or 0.
        if (raw is null) return 0;
        var end = 0;
        var trimmed = raw.TrimStart();
        if (end < trimmed.Length && trimmed[end] is '-' or '+') end++;
        while (end < trimmed.Length && char.IsAsciiDigit(trimmed[end])) end++;
        return int.TryParse(trimmed[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    /// <summary>Slot-0 tokens of a string-matched event (speech, movie), upper-cased like the engine.</summary>
    private static IEnumerable<string> StringTokensOf(StoryEvent storyEvent)
    {
        var raw = storyEvent.EventParams.FirstOrDefault(p => p.Position == 0)?.RawValue;
        return raw is null ? [] : StoryReferenceTypes.SplitList(raw).Select(t => t.ToUpperInvariant());
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
}