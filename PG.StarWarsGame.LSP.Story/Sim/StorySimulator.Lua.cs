// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Story.Graph;

namespace PG.StarWarsGame.LSP.Story.Sim;

/// <summary>One script's PGStateMachine: the state it is in, the one it will enter on the next service pass, and who asked.</summary>
public sealed record LuaScriptState(string? Current, string? Next, string? TriggeredBy, int EnteredTick)
{
    public static readonly LuaScriptState Idle = new(null, null, null, 0);

    /// <summary>Measured: Story_Event_Trigger only sets the next state while none is pending.</summary>
    public bool TransitionPending => !string.Equals(Current, Next, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A <c>Story_Event(Id)</c> a thread will make at <see cref="DueClock" />.</summary>
public sealed record LuaPendingEmission(string ScriptUri, string State, string Id, double DueClock);

/// <summary>
///     The Lua half of the simulator: the state machine each campaign script runs, as
///     <c>Pgstatemachine.lua</c> and <c>Pgstorymode.lua</c> run it (measured 2026-09-19). A fired
///     XML event whose name is a <c>StoryModeEvents</c> key sets the script's next state unless a
///     transition is already pending, in which case the trigger is dropped. The next service
///     pass, here the next tick, runs OnExit of the old state, changes state, and runs OnEnter of
///     the new one. What a phase does comes from the static extraction: its threads sleep and
///     then call <c>Story_Event</c>, which the simulator owes at the right clock and dispatches to
///     the listening <c>STORY_AI_NOTIFICATION</c> events; its spawns write presence facts.
///     Nothing is executed; the machine is emulated and the bodies are read.
/// </summary>
public sealed partial class StorySimulator
{
    /// <summary>Engine order: the Lua trigger is pushed before the event's own Triggered flag and reward.</summary>
    private StorySimSnapshot LuaTrigger(StorySimSnapshot snapshot, StoryNode eventNode)
    {
        var name = eventNode.Event!.Name;
        foreach (var machine in _model.LuaMachines)
        {
            var state = machine.States.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (state is null) continue;
            var stateId = StoryGraphBuilder.LuaStateNodeId(machine.ScriptUri, state.Name);
            var script = snapshot.Runtime.Scripts.GetValueOrDefault(machine.ScriptUri) ?? LuaScriptState.Idle;
            if (script.TransitionPending)
            {
                snapshot = Note(snapshot, stateId, eventNode.Id, StorySimCause.Ignored,
                    $"{machine.ScriptName}: trigger '{name}' dropped - a transition to '{script.Next}' is pending.");
                continue;
            }

            snapshot = snapshot with
            {
                Runtime = snapshot.Runtime.WithScript(machine.ScriptUri,
                    script with { Next = state.Name, TriggeredBy = eventNode.Id })
            };
            snapshot = Note(snapshot, stateId, eventNode.Id, StorySimCause.LuaTrigger,
                $"{machine.ScriptName}: next state '{state.Name}'.");
        }

        return snapshot;
    }

    /// <summary>The tick's service pass: pending transitions run, then the emissions that came due.</summary>
    private StorySimSnapshot ServiceScripts(StorySimSnapshot snapshot)
    {
        return DispatchDueEmissions(RunTransitions(snapshot));
    }

    /// <summary>
    ///     The engine services a script every frame, so an event fired on a later frame than the
    ///     one that set the next state finds the transition done. A command is its own frame, and
    ///     so is each polled fire; a cascade inside one fire is one frame and its extra triggers
    ///     drop, as measured.
    /// </summary>
    private StorySimSnapshot RunTransitions(StorySimSnapshot snapshot)
    {
        foreach (var machine in _model.LuaMachines)
        {
            var script = snapshot.Runtime.Scripts.GetValueOrDefault(machine.ScriptUri);
            if (script is null || !script.TransitionPending) continue;

            if (script.Current is { } current && FindState(machine, current) is { } leaving)
            {
                snapshot = RunPhase(snapshot, machine, leaving, leaving.OnExit, script.TriggeredBy);
                snapshot = Note(snapshot, StoryGraphBuilder.LuaStateNodeId(machine.ScriptUri, leaving.Name),
                    script.TriggeredBy, StorySimCause.LuaExit, $"{machine.ScriptName}: left '{leaving.Name}'.");
            }

            var entering = script.Next is { } next ? FindState(machine, next) : null;
            script = script with { Current = script.Next, EnteredTick = snapshot.Tick };
            snapshot = snapshot with { Runtime = snapshot.Runtime.WithScript(machine.ScriptUri, script) };
            if (entering is null) continue;

            snapshot = Note(snapshot, StoryGraphBuilder.LuaStateNodeId(machine.ScriptUri, entering.Name),
                script.TriggeredBy, StorySimCause.LuaEnter, $"{machine.ScriptName}: entered '{entering.Name}'.");
            snapshot = RunPhase(snapshot, machine, entering, entering.OnEnter, script.TriggeredBy);
        }

        return snapshot;
    }

    private StorySimSnapshot RunPhase(StorySimSnapshot snapshot, LuaStoryMachine machine, LuaStoryState state,
        LuaStoryPhase phase, string? triggeredBy)
    {
        var stateId = StoryGraphBuilder.LuaStateNodeId(machine.ScriptUri, state.Name);
        var runtime = snapshot.Runtime;
        foreach (var emission in phase.Emissions)
            runtime = runtime with
            {
                PendingEmissions = runtime.PendingEmissions.Add(new LuaPendingEmission(machine.ScriptUri, state.Name,
                    emission.Id, snapshot.Clock + emission.DelaySeconds))
            };
        snapshot = snapshot with { Runtime = runtime };
        if (phase.Emissions.Count > 0)
            snapshot = Note(snapshot, stateId, null, StorySimCause.Fact,
                $"  -> owes {string.Join(", ", phase.Emissions.Select(e => $"{e.Id} in {e.DelaySeconds:0.#}s"))}.");

        foreach (var spawn in phase.Spawns)
        {
            var change = new StoryWorldChange(StoryWorldChangeKind.BuildUnit)
            {
                UnitType = spawn.UnitType, Planet = spawn.Planet, Faction = _model.Faction
            };
            // A script's spawn is a presence fact, not a build: nothing listens for it.
            snapshot = WriteFacts(snapshot, change);
        }

        foreach (var target in phase.Transitions)
        {
            var script = snapshot.Runtime.Scripts.GetValueOrDefault(machine.ScriptUri) ?? LuaScriptState.Idle;
            if (script.TransitionPending) continue;
            snapshot = snapshot with
            {
                Runtime = snapshot.Runtime.WithScript(machine.ScriptUri,
                    script with { Next = target, TriggeredBy = triggeredBy })
            };
            snapshot = Note(snapshot, StoryGraphBuilder.LuaStateNodeId(machine.ScriptUri, target), stateId,
                StorySimCause.LuaTrigger, $"{machine.ScriptName}: Set_Next_State '{target}'.");
        }

        return snapshot;
    }

    private StorySimSnapshot DispatchDueEmissions(StorySimSnapshot snapshot)
    {
        var due = snapshot.Runtime.PendingEmissions.Where(e => e.DueClock <= snapshot.Clock).ToList();
        if (due.Count == 0) return snapshot;
        snapshot = snapshot with
        {
            Runtime = snapshot.Runtime with { PendingEmissions = snapshot.Runtime.PendingEmissions.RemoveRange(due) }
        };

        foreach (var emission in due)
        {
            var stateId = StoryGraphBuilder.LuaStateNodeId(emission.ScriptUri, emission.State);
            var fired = false;
            foreach (var node in _eventNodes)
            {
                if (!string.Equals(node.Event!.EventType, "STORY_AI_NOTIFICATION", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!IsActive(node, snapshot.Runtime)) continue;
                if (!NotificationIdsOf(node.Event)
                        .Any(id => id.Equals(emission.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;
                snapshot = Fire(snapshot, node, StorySimCause.Lua, stateId, 0);
                fired = true;
            }

            if (!fired)
                snapshot = Note(snapshot, stateId, null, StorySimCause.Ignored,
                    $"Story_Event(\"{emission.Id}\") from '{emission.State}' - no armed event listens.");
        }

        return snapshot;
    }

    private static LuaStoryState? FindState(LuaStoryMachine machine, string name)
    {
        return machine.States.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}