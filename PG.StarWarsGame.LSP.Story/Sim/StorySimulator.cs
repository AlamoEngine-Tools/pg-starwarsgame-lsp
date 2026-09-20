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

    /// <summary>Ticks run so far. One tick is one second of story time.</summary>
    public int Tick { get; init; }

    /// <summary>Wall time since start: one second per tick, always.</summary>
    public double Clock { get; init; }

    /// <summary>
    ///     Gameplay time: what STORY_ELAPSED accumulates. Measured: the engine's frame loop hands
    ///     the story its paused flag, and the timer skips the frames it is set on - while the
    ///     pending-battle choice is up the galaxy still runs its story but its timers stand still.
    /// </summary>
    public double GameplayClock { get; init; }

    /// <summary>Every transition since Start, in order. Seq is the index.</summary>
    public ImmutableList<StorySimStep> Steps { get; init; } = ImmutableList<StorySimStep>.Empty;

    /// <summary>The event whose breakpoint halted the last run, until the next command.</summary>
    public string? HaltedAt { get; init; }

    /// <summary>Human-readable step log, newest last.</summary>
    public ImmutableList<string> Log { get; init; } = ImmutableList<string>.Empty;
}

/// <summary>
///     One actionable choice the simulation is waiting on. <see cref="Facet" /> names the world
///     change kind that would fire the event when its type reads the world; <see cref="Options" />
///     are the candidates the event's own parameters name (planets, unit types, notification ids)
///     and <see cref="Suggested" /> is the first of them as a ready change.
/// </summary>
public sealed record StorySimIntervention(
    string Kind, // "manual" | "lua" | "tactical" | "battle"
    string NodeId,
    string EventName,
    string? EventType,
    IReadOnlyList<string> Options,
    string? Facet = null,
    StoryWorldChange? Suggested = null)
{
    /// <summary>
    ///     The pending battle itself, on the galactic level: the node is its portal, the name its
    ///     label, the options the choice (fight, autoResolve) or, once auto-resolve was chosen, the
    ///     outcome (won, lost). Resolved through the battle, never by satisfying a trigger.
    /// </summary>
    public const string BattleKind = "battle";

    /// <summary>
    ///     For a tactical decision, the battle whose outcome it waits on: inside a battle the
    ///     battle itself, on the galactic level the battle whose entry event this listener follows.
    ///     Null when no battle can be named, and the answer is a plain world change.
    /// </summary>
    public string? BattleKey { get; init; }
}

/// <summary>
///     Semantic story simulation over <see cref="StoryEvaluator" /> - no game process, no DAP.
///     Deterministic by construction: state transitions are pure and every transition is a
///     <see cref="StorySimStep" /> in the snapshot's trace.
///     Time is a tick of one second. A command (SatisfyTrigger, SetFlag, LuaNotify) is a world
///     event dispatched at once, pushes included; a tick dispatches the completions owed by the
///     previous commands and polls the clock and flag events to a fixpoint, each polled event at
///     most once per tick (perpetual events re-arm and fire again on the next tick).
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
///     the next tick dispatches. Everything else is a manual intervention: SatisfyTrigger,
///     LuaNotify for <c>STORY_AI_NOTIFICATION</c>, and tactical outcomes resolved by firing the
///     armed <c>STORY_VICTORY</c>/<c>STORY_MISSION_LOST</c>/<c>STORY_MISSION_FAILED</c> events.
/// </summary>
/// <summary>
///     How a session treats what the game decides on its own. <see cref="AssumeMediaCompletes" />:
///     a speech or movie a reward starts is owed its completion on the next tick; off, the listener
///     waits for the author, for stepping through media by hand. What the engine does on its own
///     regardless - a new speech ending the one playing, a battle's end ending every speech - is
///     not an assumption and happens either way.
/// </summary>
public sealed record StorySimOptions(bool AssumeMediaCompletes = true)
{
    public static readonly StorySimOptions Default = new();
}

public sealed partial class StorySimulator
{
    public const double ClockStepSeconds = 1;

    /// <summary>
    ///     The generic the battle summary dialog raises when it closes (measured: the dialog ends
    ///     every speech first, then raises this). No event timeout exists to model: the parser
    ///     gives a STORY_SPEECH_DONE a 60 s timeout when the XML carries none, but the engine's
    ///     timeout list is never filled or checked (measured: all three routines are empty), so a
    ///     speech-done listener waits until its speech ends, a battle ends, or the author answers.
    /// </summary>
    public const string BattleEndClosed = "battle_end_closed";

    /// <summary>MULTIMEDIA's speech is its parameter 8 (position 7); the movie in parameter 9 has no listener in the corpus.</summary>
    private const int MultimediaSpeechPosition = 7;

    private const int MaxFireDepth = 64;
    private const int MaxRunTicks = 3600;
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
    private readonly IStoryWorldSymbols? _symbols;
    private readonly Dictionary<string, string> _battleOfListener;

    /// <summary>
    ///     <paramref name="symbols" /> answers whether a planet, unit type or faction the author
    ///     names exists in the game; null skips the check (tests, static callers).
    ///     <paramref name="scope" /> is the battle this simulator runs, or null for the galactic
    ///     story: the game freezes the galaxy while a battle plays and a battle's plot files only
    ///     ever run there, so a session arms one scope's events and never the other's.
    /// </summary>
    public StorySimulator(StoryCampaignModel model, ISchemaProvider schema, IStoryWorldSymbols? symbols = null,
        string? scope = null, StorySimOptions? options = null)
    {
        _model = model;
        _symbols = symbols;
        Options = options ?? StorySimOptions.Default;
        Scope = StoryGraphScoper.IsGalactic(scope) ? null : StoryGraphScoper.BattleKey(scope!);
        // Lifecycles come from the whole graph - a prereq never crosses a scope, so the answer is
        // the same - while the events this session runs are the scope's alone.
        _evaluator = new StoryEvaluator(model.Graph);
        _eventNodes = StoryGraphScoper.Scope(model, Scope).Nodes.Where(n => n.Kind == StoryNodeKind.Event).ToList();
        // On the galactic level an outcome listener belongs to the battle whose entry event it
        // follows - directly, through a junction, or behind the summary-closed listener, as the
        // tutorial writes its failure branch; inside a battle every outcome listener is the
        // battle's own.
        _battleOfListener = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Scope is null)
            foreach (var battle in model.Battles)
            foreach (var entry in battle.EntryEventIds)
            foreach (var listener in StoryGraphScoper.OutcomeListenerIds(model.Graph, entry))
                _battleOfListener.TryAdd(listener, battle.Key);
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

    /// <summary>The battle this simulator runs, as the plots feed keys it, or null for the galactic story.</summary>
    public string? Scope { get; }

    public StorySimOptions Options { get; }

    /// <summary>
    ///     What a battle can write into the shared flag table: every flag its events' SET_FLAG and
    ///     INCREMENT_FLAG rewards name, with the value the reward would set or add - the picks a
    ///     portal offers when the battle is decided without being played. First reward per flag.
    /// </summary>
    public static IReadOnlyList<StoryFlagWrite> FlagWritesOf(StoryCampaignModel model, ISchemaProvider schema,
        string scope)
    {
        var rewardParamTypes = StoryReferenceTypes.BuildParamMap(schema.GetEnum("StoryRewardType"));
        var writes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in StoryGraphScoper.Scope(model, scope).Nodes.Where(n => n.Kind == StoryNodeKind.Event))
        {
            var storyEvent = node.Event!;
            var reward = storyEvent.RewardType?.ToUpperInvariant();
            if (reward is null) continue;
            var rawAmount = storyEvent.RewardParams.FirstOrDefault(p => p.Position == 1)?.RawValue;
            var amount = int.TryParse(rawAmount, out var parsed) ? parsed : 1;
            foreach (var slot in storyEvent.RewardParams)
            {
                if (rewardParamTypes.GetValueOrDefault((reward, slot.Position)) != StoryReferenceTypes.Flag) continue;
                writes.TryAdd(slot.RawValue.Trim(), amount);
            }
        }

        return writes.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new StoryFlagWrite(kvp.Key, kvp.Value)).ToList();
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>
    ///     The first frame. <paramref name="flags" /> and <paramref name="world" /> seed the run
    ///     before anything polls - a battle starts with the galaxy's flag table and world as they
    ///     stand when it is entered; without them the campaign's own seed applies.
    /// </summary>
    public StorySimSnapshot Start(ImmutableDictionary<string, int>? flags = null, StoryWorld? world = null)
    {
        var snapshot = new StorySimSnapshot
        {
            Runtime = StoryRuntimeState.Initial with
            {
                SuspendedThreads = StoryRuntimeState.Initial.SuspendedThreads.Union(_model.SuspendedThreadUris),
                ArmedEvents = ImmutableHashSet.Create<string>(StringComparer.Ordinal),
                World = world ?? SeedWorld(_model.Seed),
                Flags = flags ?? StoryRuntimeState.Initial.Flags
            },
            Log = ImmutableList.Create(Scope is null ? "Simulation started" : $"Battle {Scope} started")
                .AddRange(_startupNotes)
        };

        // The parser arms every event that has no prereq line; everything else waits for a push.
        foreach (var node in _eventNodes)
            if (node.Event!.PrereqGroups.Count == 0)
                snapshot = Transition(snapshot, node, null, StorySimCause.Load, null,
                    r => r.WithArmed(node.Id, 0));

        // Tick 0 is the first frame: whatever is due at once (STORY_ELAPSED 0) fires now.
        return Poll(snapshot);
    }

    /// <summary>Runs <paramref name="count" /> ticks, stopping early at a breakpoint.</summary>
    public StorySimSnapshot Tick(StorySimSnapshot snapshot, int count = 1, StorySimBreakpoints? breakpoints = null)
    {
        breakpoints ??= StorySimBreakpoints.None;
        snapshot = snapshot with { HaltedAt = null };
        for (var i = 0; i < count; i++)
        {
            snapshot = TickOnce(snapshot, breakpoints);
            if (snapshot.HaltedAt is not null) break;
        }

        return snapshot;
    }

    /// <summary>
    ///     Ticks until the story waits on the user (an intervention appears), a breakpoint halts
    ///     the run, or a tick changes nothing - which is a finished or stuck story, not a reason to
    ///     spin.
    /// </summary>
    public StorySimSnapshot RunToDecision(StorySimSnapshot snapshot, StorySimBreakpoints breakpoints)
    {
        snapshot = snapshot with { HaltedAt = null };
        // A real campaign has hundreds of armed world listeners from tick 0, each a standing
        // decision; stopping at "any decision" would never tick. The run stops at a NEW one.
        var standing = GetInterventions(snapshot).Select(i => i.NodeId).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < MaxRunTicks; i++)
        {
            var before = snapshot.Steps.Count;
            snapshot = TickOnce(snapshot, breakpoints);
            if (snapshot.HaltedAt is not null) break;
            if (GetInterventions(snapshot).Any(d => !standing.Contains(d.NodeId))) break;
            if (snapshot.Steps.Count == before) break;
        }

        return snapshot;
    }

    /// <summary>
    ///     What the clock alone can still change: armed timers, owed speech and movie completions,
    ///     and script work (owed emissions, pending state changes). Zero means nothing more happens
    ///     until the author answers a decision - the state the dock reports as waiting on them.
    /// </summary>
    public int GetClockPending(StorySimSnapshot snapshot)
    {
        var runtime = snapshot.Runtime;
        // Gameplay time stands still while a battle is pending, so no timer is the clock's to end.
        var timers = runtime.World.PendingBattle is not null
            ? 0
            : _eventNodes.Count(node =>
                IsActive(node, runtime)
                && string.Equals(node.Event!.EventType, "STORY_ELAPSED", StringComparison.OrdinalIgnoreCase));
        var scripts = runtime.Scripts.Values.Count(s => s.TransitionPending);
        // A flag poll that already holds fires on the next tick: the clock's, not the author's.
        var polls = _eventNodes.Count(node =>
            IsActive(node, runtime)
            && string.Equals(node.Event!.EventType, "STORY_FLAG", StringComparison.OrdinalIgnoreCase)
            && PolledTriggerSatisfied(node, snapshot));
        // Media left to the author is playing, not owed: the clock does nothing for it.
        var completions = Options.AssumeMediaCompletes ? runtime.PendingCompletions.Count : 0;
        return timers + polls + completions + runtime.PendingEmissions.Count + scripts;
    }

    /// <summary>Convenience over <see cref="Tick" />: whole seconds become ticks.</summary>
    public StorySimSnapshot AdvanceClock(StorySimSnapshot snapshot, double seconds)
    {
        var ticks = (int)Math.Round(seconds / ClockStepSeconds);
        return ticks <= 0 ? snapshot : Tick(snapshot, ticks);
    }

    /// <summary>Manually fires an active event (the user says its trigger condition happened).</summary>
    public StorySimSnapshot SatisfyTrigger(StorySimSnapshot snapshot, string nodeId)
    {
        snapshot = RunTransitions(snapshot with { HaltedAt = null });
        if (!_nodesById.TryGetValue(nodeId, out var node))
            return snapshot with { Log = snapshot.Log.Add($"Unknown event node '{nodeId}'.") };
        if (!IsActive(node, snapshot.Runtime))
            return Note(snapshot, node.Id, null, StorySimCause.Ignored,
                $"'{node.Event!.Name}' is not armed - trigger ignored.");

        return Fire(snapshot, node, StorySimCause.Manual, null, 0);
    }

    /// <summary>
    ///     The author's call that an armed listener will not fire in this run (or, with
    ///     <paramref name="ruledOut" /> false, that it may after all). Nothing changes for the
    ///     engine - the listener stays armed - only the decision list.
    /// </summary>
    public StorySimSnapshot RuleOut(StorySimSnapshot snapshot, string nodeId, bool ruledOut)
    {
        snapshot = snapshot with { HaltedAt = null };
        if (!_nodesById.TryGetValue(nodeId, out var node))
            return snapshot with { Log = snapshot.Log.Add($"Unknown event node '{nodeId}'") };
        var name = node.Event!.Name;
        if (ruledOut == snapshot.Runtime.RuledOut.Contains(nodeId))
            return Note(snapshot, nodeId, null, StorySimCause.Ignored,
                ruledOut ? $"'{name}' is already ruled out" : $"'{name}' is not ruled out");
        var runtime = snapshot.Runtime with
        {
            RuledOut = ruledOut ? snapshot.Runtime.RuledOut.Add(nodeId) : snapshot.Runtime.RuledOut.Remove(nodeId)
        };
        return Note(snapshot with { Runtime = runtime }, nodeId, null,
            ruledOut ? StorySimCause.RuledOut : StorySimCause.Reconsidered,
            ruledOut ? $"'{name}' ruled out - armed, no decision" : $"'{name}' reconsidered - a decision again");
    }

    public StorySimSnapshot SetFlag(StorySimSnapshot snapshot, string flag, int value)
    {
        return snapshot with
        {
            HaltedAt = null,
            Runtime = snapshot.Runtime.WithFlag(flag, value),
            Log = snapshot.Log.Add($"Flag {flag} = {value}.")
        };
    }

    /// <summary>Simulates Lua calling <c>Story_Event("id")</c>: fires active AI-notification events with that id.</summary>
    public StorySimSnapshot LuaNotify(StorySimSnapshot snapshot, string notificationId)
    {
        snapshot = RunTransitions(snapshot with
        {
            HaltedAt = null, Log = snapshot.Log.Add($"Lua Story_Event(\"{notificationId}\").")
        });
        var fired = false;
        foreach (var node in _eventNodes)
        {
            if (!string.Equals(node.Event!.EventType, "STORY_AI_NOTIFICATION", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsActive(node, snapshot.Runtime)) continue;
            if (!NotificationIdsOf(node.Event).Any(id =>
                    string.Equals(id, notificationId, StringComparison.OrdinalIgnoreCase))) continue;
            snapshot = Fire(snapshot, node, StorySimCause.Lua, null, 0);
            fired = true;
        }

        if (!fired)
            snapshot = snapshot with { Log = snapshot.Log.Add($"No armed event listens for '{notificationId}'.") };
        return snapshot;
    }

    // ── Read model ───────────────────────────────────────────────────────────

    public IReadOnlyDictionary<string, StoryEventLifecycle> GetLifecycles(StorySimSnapshot snapshot)
    {
        return _eventNodes.ToDictionary(n => n.Id, n => _evaluator.GetLifecycle(n.Id, snapshot.Runtime),
            StringComparer.Ordinal);
    }

    /// <summary>The battle a tactical listener waits on: the one it is inside, or the one whose link it follows. Null for any other event.</summary>
    public string? BattleOf(string nodeId)
    {
        return Scope ?? _battleOfListener.GetValueOrDefault(nodeId);
    }

    /// <summary>What the simulation is waiting on: active events whose trigger the model cannot fire itself.</summary>
    public IReadOnlyList<StorySimIntervention> GetInterventions(StorySimSnapshot snapshot)
    {
        var interventions = new List<StorySimIntervention>();
        // The pending battle is the author's decision before any listener's: fight or auto-resolve
        // while the choice is up; then won or lost once auto-resolve was chosen; and once the
        // fight is chosen, with the galaxy frozen, whether to play the battle in its own panel or
        // skip it with an outcome and the flags it would have written.
        if (snapshot.Runtime.World is { PendingBattle: { } pendingKey })
        {
            var choice = snapshot.Runtime.World.PendingBattleChoice;
            var label = _model.Battles.FirstOrDefault(b => b.Key == pendingKey)?.Label ?? pendingKey;
            IReadOnlyList<string> options = choice switch
            {
                null => [StoryBattleChoice.Fight, StoryBattleChoice.AutoResolve],
                StoryBattleChoice.Fight => ["enter", "won", "lost"],
                _ => ["won", "lost"]
            };
            interventions.Add(new StorySimIntervention(StorySimIntervention.BattleKind,
                StoryGraphScoper.TacticalNodeId(pendingKey), label, null, options)
            {
                BattleKey = pendingKey
            });
        }

        foreach (var node in _eventNodes)
        {
            if (!IsActive(node, snapshot.Runtime)) continue;
            // Ruled out by the author for this run: armed, but nothing to answer.
            if (snapshot.Runtime.RuledOut.Contains(node.Id)) continue;
            var storyEvent = node.Event!;
            var type = storyEvent.EventType?.ToUpperInvariant();
            // Polled types fire on their own; a STORY_TRIGGER only ever fires from a push.
            if (type is "STORY_ELAPSED" or "STORY_TRIGGER" or "STORY_FLAG") continue;
            // A generic the game never raises fires only from a TRIGGER_EVENT push, never from
            // anything the player does: nothing for the author to answer. Continue_Tutorial is
            // raised by the tutorial dialog's button alone, so it counts only while one shows.
            if (type == "STORY_GENERIC")
            {
                var names = StringTokensOf(storyEvent).ToList();
                var dialogUp = snapshot.Runtime.World.TutorialDialog is not null;
                if (names.Count > 0 && !names.Any(n => StoryGenericNames.IsEngineRaised(n)
                                                       && (dialogUp || !n.Equals(
                                                           StoryGraphBuilder.ContinueTutorialGeneric,
                                                           StringComparison.OrdinalIgnoreCase))))
                    continue;
            }

            // The summary's generic is raised by the battle's resolution while one is pending or
            // running - not a thing the author answers by hand meanwhile.
            if (type == "STORY_GENERIC" && snapshot.Runtime.World.PendingBattle is not null
                                        && StringTokensOf(storyEvent).Any(t =>
                                            t.Equals(BattleEndClosed, StringComparison.OrdinalIgnoreCase)))
                continue;
            // A completion the simulator owes will dispatch on the next tick; media left to the
            // author is theirs to end, so its listener stays a decision.
            if (Options.AssumeMediaCompletes && type is SpeechDone or MovieDone &&
                HasPendingCompletion(node, snapshot.Runtime)) continue;

            var kind = type switch
            {
                "STORY_AI_NOTIFICATION" => "lua",
                "STORY_VICTORY" or "STORY_MISSION_LOST" or "STORY_MISSION_FAILED" => "tactical",
                _ => "manual"
            };
            if (kind == "lua")
            {
                interventions.Add(new StorySimIntervention(kind, node.Id, storyEvent.Name,
                    storyEvent.EventType, NotificationIdsOf(storyEvent).ToList()));
                continue;
            }

            var battleKey = kind == "tactical" ? Scope ?? _battleOfListener.GetValueOrDefault(node.Id) : null;
            // Once the galaxy has taken a battle's outcome, that outcome was dispatched once and the
            // other never will be: a listener still armed - behind the summary listener, as the
            // tutorial's loss branch is (measured: the loss reaches the galaxy as a delayed event
            // before the summary closes) - missed it, and nothing the author decides brings it back.
            if (battleKey is not null && snapshot.Runtime.World.BattleOutcomes.ContainsKey(battleKey))
                continue;

            var (facet, options, suggested) = FacetOf(storyEvent);
            interventions.Add(new StorySimIntervention(kind, node.Id, storyEvent.Name,
                storyEvent.EventType, options, facet, suggested) { BattleKey = battleKey });
        }

        return interventions;
    }

    /// <summary>
    ///     How far an armed clock or flag gate has come: "4/10 s" with 0.4, "FLAG_X 2 of 3" with
    ///     0.67, "FLAG_X unset" with 0. Null for anything that is not armed or has no such gate.
    /// </summary>
    public StorySimGate? GetGate(StorySimSnapshot snapshot, string nodeId)
    {
        if (!_nodesById.TryGetValue(nodeId, out var node) || !IsActive(node, snapshot.Runtime)) return null;
        var storyEvent = node.Event!;
        switch (storyEvent.EventType?.ToUpperInvariant())
        {
            case "STORY_ELAPSED":
            {
                var raw = storyEvent.EventParams.FirstOrDefault(p => p.Position == 0)?.RawValue;
                if (raw is null || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var at))
                    return null;
                var elapsed = Math.Max(0, snapshot.GameplayClock - snapshot.Runtime.ArmedAt.GetValueOrDefault(node.Id));
                return new StorySimGate($"{elapsed:0}/{at:0} s", at <= 0 ? 1 : Math.Min(1, elapsed / at));
            }
            case "STORY_FLAG":
            {
                var flag = FlagsReadBy(storyEvent).FirstOrDefault();
                if (flag is null) return null;
                var rawTarget = storyEvent.EventParams.FirstOrDefault(p => p.Position == 1)?.RawValue;
                var target = int.TryParse(rawTarget, out var parsed) ? parsed : 0;
                if (!snapshot.Runtime.Flags.TryGetValue(flag, out var value))
                    return new StorySimGate($"{flag} unset", 0);
                var progress = target <= 0 ? (value == target ? 1 : 0) : Math.Clamp((double)value / target, 0, 1);
                return new StorySimGate($"{flag} {value} of {target}", progress);
            }
            default:
                return null;
        }
    }

    // ── Tick ─────────────────────────────────────────────────────────────────

    private StorySimSnapshot TickOnce(StorySimSnapshot snapshot, StorySimBreakpoints breakpoints)
    {
        var firstSeq = snapshot.Steps.Count;
        var paused = snapshot.Runtime.World.PendingBattle is not null;
        snapshot = snapshot with
        {
            Tick = snapshot.Tick + 1,
            Clock = snapshot.Clock + ClockStepSeconds,
            GameplayClock = paused ? snapshot.GameplayClock : snapshot.GameplayClock + ClockStepSeconds,
            Log = snapshot.Log.Add(paused
                ? $"Tick {snapshot.Tick + 1} ({snapshot.Clock + ClockStepSeconds:0.##}s, gameplay paused for the battle)"
                : $"Tick {snapshot.Tick + 1} ({snapshot.Clock + ClockStepSeconds:0.##}s)")
        };
        snapshot = DispatchCompletions(snapshot);
        snapshot = ServiceScripts(snapshot);
        snapshot = Poll(snapshot);

        // A breakpoint halts AFTER the tick in which its event fired - a frame cannot stop halfway.
        var hit = snapshot.Steps.Skip(firstSeq).FirstOrDefault(s =>
            s.To == StoryEventLifecycle.Fired && StorySimCause.Fires.Contains(s.Cause) &&
            (breakpoints.NodeIds.Contains(s.NodeId) ||
             (breakpoints.OnConditionalGates && s.Cause == StorySimCause.Poll)));
        if (hit is null) return snapshot;

        snapshot = Note(snapshot, hit.NodeId, null, StorySimCause.Breakpoint,
            $"Halted at '{EventNameOf(hit.NodeId)}'.");
        return snapshot with { HaltedAt = hit.NodeId };
    }

    /// <summary>Polls the clock and flag events to a fixpoint; each polled node fires at most once per pass.</summary>
    private StorySimSnapshot Poll(StorySimSnapshot snapshot)
    {
        var firedThisPass = new HashSet<string>(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var node in _eventNodes)
            {
                if (firedThisPass.Contains(node.Id)) continue;
                if (!IsActive(node, snapshot.Runtime)) continue;
                if (!PolledTriggerSatisfied(node, snapshot)) continue;

                // Each polled fire is its own frame for the scripts.
                snapshot = Fire(RunTransitions(snapshot), node, StorySimCause.Poll, null, 0);
                firedThisPass.Add(node.Id);
                changed = true;
            }
        } while (changed);

        return snapshot;
    }

    private StorySimSnapshot DispatchCompletions(StorySimSnapshot snapshot)
    {
        // Media left to the author plays until they end it (or the engine does, on a new speech
        // or a battle's end); the clock never completes it.
        if (!Options.AssumeMediaCompletes) return snapshot;
        var pending = snapshot.Runtime.PendingCompletions;
        if (pending.Count == 0) return snapshot;

        // Consume first: the engine dispatches once and an event not active at that moment misses it.
        snapshot = snapshot with { Runtime = snapshot.Runtime.WithoutCompletions(pending) };
        var owed = pending.Select(ParseCompletion).ToList();
        foreach (var node in _eventNodes)
        {
            if (!IsActive(node, snapshot.Runtime)) continue;
            var type = node.Event!.EventType?.ToUpperInvariant();
            if (type is not (SpeechDone or MovieDone)) continue;
            var tokens = StringTokensOf(node.Event).ToList();
            var match = owed.FirstOrDefault(o => o.Type == type && tokens.Contains(o.Name));
            if (match == default) continue;
            snapshot = Fire(snapshot, node, type == SpeechDone ? StorySimCause.Speech : StorySimCause.Movie,
                match.OwnerNodeId, 0);
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
                return snapshot.GameplayClock - armedAt >= at;
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

    // ── Event machine ────────────────────────────────────────────────────────

    /// <summary>Engine Event_Triggered: swallowed when disabled, else reward, push dependants, perpetual re-arm.</summary>
    private StorySimSnapshot Fire(StorySimSnapshot snapshot, StoryNode node, string cause, string? sourceId, int depth)
    {
        var storyEvent = node.Event!;
        if (depth > MaxFireDepth)
            return Note(snapshot, node.Id, sourceId, StorySimCause.Ignored,
                $"'{storyEvent.Name}' not fired - trigger chain deeper than {MaxFireDepth}.");
        if (snapshot.Runtime.DisabledEvents.Contains(node.Id))
            return Note(snapshot, node.Id, sourceId, StorySimCause.Ignored,
                $"'{storyEvent.Name}' is disabled - fire swallowed.");

        // Measured order: the plot's Lua script hears Story_Event_Trigger before the event's own
        // Triggered flag and reward.
        snapshot = LuaTrigger(snapshot, node);

        // The fire step always lands on Fired, even for a perpetual event whose lifecycle reads
        // Armed again the moment it re-arms: the engine sets Triggered here and clears it later.
        snapshot = Transition(snapshot, node, sourceId, cause, $"Fired '{storyEvent.Name}' ({cause}).",
            r => r.WithFired(node.Id) with { RuledOut = r.RuledOut.Remove(node.Id) }, StoryEventLifecycle.Fired);
        snapshot = GiveReward(snapshot, node, depth);

        if (_dependantsById.TryGetValue(node.Id, out var dependants))
            foreach (var dependant in dependants)
                snapshot = ParentTriggered(snapshot, dependant, node.Id, depth + 1);

        if (!storyEvent.Perpetual) return snapshot;

        // Perpetual: Triggered cleared, Reset (drops arming when it has prereqs), then Active
        // again for every type but STORY_TRIGGER, which waits for the next push.
        var clock = snapshot.GameplayClock;
        return Transition(snapshot, node, null, StorySimCause.Perpetual, null, r =>
        {
            r = r.WithUnfired(node.Id);
            if (storyEvent.PrereqGroups.Count > 0) r = r.WithUnarmed(node.Id);
            return IsStoryTrigger(storyEvent) ? r : r.WithArmed(node.Id, clock);
        });
    }

    /// <summary>Engine Parent_Triggered: arm when a line is fully fired; a STORY_TRIGGER fires at once.</summary>
    private StorySimSnapshot ParentTriggered(StorySimSnapshot snapshot, StoryNode node, string sourceId, int depth)
    {
        var runtime = snapshot.Runtime;
        if (runtime.ArmedEvents?.Contains(node.Id) == true || runtime.FiredEvents.Contains(node.Id))
            return snapshot;

        var lines = _prereqLinesById[node.Id];
        if (!lines.Any(line => line.All(runtime.FiredEvents.Contains))) return snapshot;

        snapshot = Transition(snapshot, node, sourceId, StorySimCause.Prereq, $"  -> armed '{node.Event!.Name}'.",
            r => r.WithArmed(node.Id, snapshot.GameplayClock));
        return IsStoryTrigger(node.Event) ? Fire(snapshot, node, StorySimCause.Prereq, sourceId, depth) : snapshot;
    }

    private StorySimSnapshot GiveReward(StorySimSnapshot snapshot, StoryNode node, int depth)
    {
        var storyEvent = node.Event!;
        if (storyEvent.RewardType is not { } rewardType) return snapshot;
        var upperReward = rewardType.ToUpperInvariant();

        snapshot = ApplyFlagRewards(snapshot, node, upperReward);

        return upperReward switch
        {
            "STORY_ELEMENT" => ActivateThread(snapshot, node),
            "TRIGGER_EVENT" => RewardTriggerEvent(snapshot, node, depth),
            "RESET_EVENT" => RewardResetEvent(snapshot, node),
            "RESET_BRANCH" => RewardResetBranch(snapshot, node, depth),
            "DISABLE_STORY_EVENT" => RewardDisableStoryEvent(snapshot, node),
            "DISABLE_BRANCH" => RewardDisableBranch(snapshot, node),
            "SPEECH" => OweCompletion(snapshot, node, SpeechDone, depth),
            "START_MOVIE" => OweCompletion(snapshot, node, MovieDone, depth),
            // Measured: the compound reward shows its text, plays the speech in parameter 8 when
            // there is one and the command-bar movie in parameter 9 when there is one; without a
            // speech there is nothing to owe and the reward is done at once.
            "MULTIMEDIA" => OweCompletion(snapshot, node, SpeechDone, depth, MultimediaSpeechPosition),
            _ => ApplyWorldReward(snapshot, node, upperReward, depth)
        };
    }

    private StorySimSnapshot ApplyFlagRewards(StorySimSnapshot snapshot, StoryNode node, string upperReward)
    {
        // Flag-writing rewards (schema StoryFlag params on the reward side). SET_FLAG writes
        // param 1 (default 1), INCREMENT_FLAG adds param 1 (default 1, may be negative), other
        // flag-writing rewards conservatively set 1.
        var storyEvent = node.Event!;
        var rawAmount = storyEvent.RewardParams.FirstOrDefault(p => p.Position == 1)?.RawValue;
        var amount = int.TryParse(rawAmount, out var parsedAmount) ? parsedAmount : 1;
        foreach (var slot in storyEvent.RewardParams)
        {
            if (_rewardParamTypes.GetValueOrDefault((upperReward, slot.Position)) != StoryReferenceTypes.Flag)
                continue;
            foreach (var flag in StoryReferenceTypes.SplitList(slot.RawValue))
            {
                var value = upperReward switch
                {
                    "INCREMENT_FLAG" => snapshot.Runtime.Flags.GetValueOrDefault(flag) + amount,
                    "SET_FLAG" => amount,
                    _ => 1
                };
                snapshot = Note(snapshot with { Runtime = snapshot.Runtime.WithFlag(flag, value) },
                    node.Id, null, StorySimCause.Flag, $"  -> flag {flag} = {value}.");
            }
        }

        return snapshot;
    }

    // STORY_ELEMENT activates a suspended thread (param = thread name sans .xml).
    private static StorySimSnapshot ActivateThread(StorySimSnapshot snapshot, StoryNode node)
    {
        var element = Param(node.Event!, 0);
        if (element is null) return snapshot;
        var suffix = "/" + element.ToLowerInvariant() + ".xml";
        var match = snapshot.Runtime.SuspendedThreads.FirstOrDefault(u =>
            u.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (match is null) return snapshot;
        return Note(
            snapshot with
            {
                Runtime = snapshot.Runtime with
                {
                    SuspendedThreads = snapshot.Runtime.SuspendedThreads.Remove(match)
                }
            },
            node.Id, null, StorySimCause.Thread, $"  -> activated thread '{element}'.");
    }

    // Measured: Trigger_Event(name) runs Event_Triggered on the named event in EVERY subplot,
    // with no prereq, armed or fired check - a waiting event fires, a fired one fires again.
    private StorySimSnapshot RewardTriggerEvent(StorySimSnapshot snapshot, StoryNode node, int depth)
    {
        var name = Param(node.Event!, 0);
        if (name is null)
            return Note(snapshot, node.Id, null, StorySimCause.Ignored,
                "  -> TRIGGER_EVENT ignored - missing parameter.");
        if (!_eventsByName.TryGetValue(name, out var targets))
            return Note(snapshot, node.Id, null, StorySimCause.Ignored,
                $"  -> TRIGGER_EVENT: no event named '{name}'.");
        foreach (var target in targets)
            snapshot = Fire(snapshot, target, StorySimCause.Trigger, node.Id, depth + 1);
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
                snapshot = Note(snapshot, node.Id, null, StorySimCause.Ignored,
                    $"  -> RESET_EVENT: no event '{name}' in this thread.");
                continue;
            }

            snapshot = Transition(snapshot, target, node.Id, StorySimCause.Reset,
                $"  -> reset '{target.Event!.Name}'.", r => ClearTriggered(r, target));
        }

        return snapshot;
    }

    // Measured: pass 1 clears every member of the branch, pass 2 re-pushes each (a STORY_TRIGGER
    // member whose prereqs are still fired fires again), then param 1 triggers a named event in
    // this thread.
    private StorySimSnapshot RewardResetBranch(StorySimSnapshot snapshot, StoryNode node, int depth)
    {
        var storyEvent = node.Event!;
        var branch = Param(storyEvent, 0);
        if (branch is null)
            return Note(snapshot, node.Id, null, StorySimCause.Ignored,
                "  -> RESET_BRANCH ignored - missing parameter.");

        var members = BranchMembers(node, branch);
        foreach (var member in members)
            snapshot = Transition(snapshot, member, node.Id, StorySimCause.Reset,
                $"  -> reset '{member.Event!.Name}' (branch '{branch}').", r => ClearTriggered(r, member));
        foreach (var member in members) snapshot = ParentTriggered(snapshot, member, node.Id, depth + 1);

        if (Param(storyEvent, 1) is not { } named) return snapshot;
        var target = SameThreadEvent(node, named);
        if (target is null)
            return Note(snapshot, node.Id, null, StorySimCause.Ignored,
                $"  -> RESET_BRANCH: no event '{named}' in this thread.");
        return Fire(snapshot, target, StorySimCause.Trigger, node.Id, depth + 1);
    }

    // Measured: params 0 and 1 are both required or the reward is ignored; Disabled =
    // atoi(param 1) != 0; a non-zero param 2 disables the name in every thread.
    private StorySimSnapshot RewardDisableStoryEvent(StorySimSnapshot snapshot, StoryNode node)
    {
        var storyEvent = node.Event!;
        var name = Param(storyEvent, 0);
        var flag = Param(storyEvent, 1);
        if (name is null || flag is null)
            return Note(snapshot, node.Id, null, StorySimCause.Ignored,
                "  -> DISABLE_STORY_EVENT ignored - missing parameter.");

        var disable = ParseInt(flag) != 0;
        var everywhere = ParseInt(Param(storyEvent, 2)) != 0;
        var targets = everywhere
            ? _eventsByName.GetValueOrDefault(name) ?? []
            : SameThreadEvent(node, name) is { } single
                ? [single]
                : [];
        if (targets.Count == 0)
            return Note(snapshot, node.Id, null, StorySimCause.Ignored,
                $"  -> DISABLE_STORY_EVENT: no event named '{name}'.");

        foreach (var target in targets) snapshot = SetDisabled(snapshot, target, node.Id, disable);
        return snapshot;
    }

    // Measured: both params required; Disabled = atoi(param 1) != 0 on every member, this thread only.
    private StorySimSnapshot RewardDisableBranch(StorySimSnapshot snapshot, StoryNode node)
    {
        var storyEvent = node.Event!;
        var branch = Param(storyEvent, 0);
        var flag = Param(storyEvent, 1);
        if (branch is null || flag is null)
            return Note(snapshot, node.Id, null, StorySimCause.Ignored,
                "  -> DISABLE_BRANCH ignored - missing parameter.");

        var disable = ParseInt(flag) != 0;
        foreach (var member in BranchMembers(node, branch)) snapshot = SetDisabled(snapshot, member, node.Id, disable);
        return snapshot;
    }

    private StorySimSnapshot SetDisabled(StorySimSnapshot snapshot, StoryNode target, string sourceId, bool disable)
    {
        return disable
            ? Transition(snapshot, target, sourceId, StorySimCause.Disable, $"  -> disabled '{target.Event!.Name}'.",
                r => r.WithDisabled(target.Id))
            : Transition(snapshot, target, sourceId, StorySimCause.Enable, $"  -> enabled '{target.Event!.Name}'.",
                r => r.WithEnabled(target.Id));
    }

    private StorySimSnapshot OweCompletion(StorySimSnapshot snapshot, StoryNode node, string doneType, int depth,
        int position = 0)
    {
        var name = Param(node.Event!, position);
        if (name is null) return snapshot;
        var cause = doneType == SpeechDone ? StorySimCause.Speech : StorySimCause.Movie;
        // Measured: a speech reward stops the conversation and kills every active speech event
        // before it queues its own, and each killed speech reports done to the story - so the
        // listeners of the speech still playing fire here, inside this reward, whatever the
        // session assumes about media.
        if (doneType == SpeechDone) snapshot = EndPlayingSpeeches(snapshot, depth);
        // The speech now playing is remembered either way; only whether the clock ends it differs.
        snapshot = snapshot with
        {
            Runtime = snapshot.Runtime.WithCompletionPending(doneType + "|" + name.ToUpperInvariant() + "|" + node.Id)
        };
        return Options.AssumeMediaCompletes
            ? Note(snapshot, node.Id, null, cause, $"  -> {doneType} '{name}' owed on the next tick.")
            : Note(snapshot, node.Id, null, cause, $"  -> {doneType} '{name}' left to the author.");
    }

    /// <summary>
    ///     The engine's kill of every playing speech with story callbacks on: each killed speech's
    ///     active listeners fire at once, and nothing is owed for it any more.
    /// </summary>
    private StorySimSnapshot EndPlayingSpeeches(StorySimSnapshot snapshot, int depth)
    {
        var playing = snapshot.Runtime.PendingCompletions.Where(k => ParseCompletion(k).Type == SpeechDone).ToList();
        if (playing.Count == 0) return snapshot;
        snapshot = snapshot with { Runtime = snapshot.Runtime.WithoutCompletions(playing) };
        var killed = playing.Select(ParseCompletion).ToList();
        foreach (var listener in _eventNodes)
        {
            if (!IsActive(listener, snapshot.Runtime)) continue;
            if (!string.Equals(listener.Event!.EventType, SpeechDone, StringComparison.OrdinalIgnoreCase)) continue;
            var tokens = StringTokensOf(listener.Event).ToList();
            var match = killed.FirstOrDefault(k => tokens.Contains(k.Name));
            if (match == default) continue;
            snapshot = Fire(snapshot, listener, StorySimCause.Speech, match.OwnerNodeId, depth + 1);
        }

        return snapshot;
    }

    private static (string Type, string Name, string OwnerNodeId) ParseCompletion(string key)
    {
        var parts = key.Split('|', 3);
        return (parts[0], parts[1], parts.Length > 2 ? parts[2] : "");
    }

    // Measured: Clear_Triggered drops Triggered and Reset drops Active when the event has prereqs.
    private static StoryRuntimeState ClearTriggered(StoryRuntimeState runtime, StoryNode node)
    {
        runtime = runtime.WithUnfired(node.Id);
        return node.Event!.PrereqGroups.Count > 0 ? runtime.WithUnarmed(node.Id) : runtime;
    }

    // ── Trace ────────────────────────────────────────────────────────────────

    /// <summary>Applies a lifecycle-changing mutation and records the step with the lifecycle before and after.</summary>
    private StorySimSnapshot Transition(StorySimSnapshot snapshot, StoryNode node, string? sourceId, string cause,
        string? logLine, Func<StoryRuntimeState, StoryRuntimeState> mutate, StoryEventLifecycle? forcedTo = null)
    {
        var from = _evaluator.GetLifecycle(node.Id, snapshot.Runtime);
        var runtime = mutate(snapshot.Runtime);
        var to = forcedTo ?? _evaluator.GetLifecycle(node.Id, runtime);
        var step = new StorySimStep(snapshot.Tick, snapshot.Steps.Count, node.Id, from, to, sourceId, cause);
        return snapshot with
        {
            Runtime = runtime,
            Steps = snapshot.Steps.Add(step),
            Log = logLine is null ? snapshot.Log : snapshot.Log.Add(logLine)
        };
    }

    /// <summary>Records a step that is not a lifecycle change (the detail is also the log line).</summary>
    private static StorySimSnapshot Note(StorySimSnapshot snapshot, string nodeId, string? sourceId, string cause,
        string detail)
    {
        var step = new StorySimStep(snapshot.Tick, snapshot.Steps.Count, nodeId, null, null, sourceId, cause, detail);
        return snapshot with { Steps = snapshot.Steps.Add(step), Log = snapshot.Log.Add(detail) };
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

    private string EventNameOf(string nodeId)
    {
        return _nodesById.TryGetValue(nodeId, out var node) ? node.Event!.Name : nodeId;
    }

    private static bool HasPendingCompletion(StoryNode node, StoryRuntimeState runtime)
    {
        var type = node.Event!.EventType!.ToUpperInvariant();
        var tokens = StringTokensOf(node.Event).ToList();
        return runtime.PendingCompletions.Select(ParseCompletion).Any(o => o.Type == type && tokens.Contains(o.Name));
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