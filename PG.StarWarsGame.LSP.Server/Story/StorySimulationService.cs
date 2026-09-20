// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     What a simulation session is OF: a campaign faction, at the galactic level (null scope) or
///     inside one of its battles (the battle's key as the plots feed names it). The scope is
///     normalised on construction so a request may spell the manifest any way the XML does.
/// </summary>
public sealed record StorySimKey(string Campaign, string Faction, string? Scope = null)
{
    public string? Scope { get; init; } =
        StoryGraphScoper.IsGalactic(Scope) ? null : StoryGraphScoper.BattleKey(Scope!);

    public StoryModelKey Model => new(Campaign, Faction);

    public bool IsGalactic => Scope is null;

    /// <summary>The galactic session this one belongs to - itself when it is galactic.</summary>
    public StorySimKey Galactic => this with { Scope = null };

    public override string ToString()
    {
        return IsGalactic ? Model.ToString() : $"{Model} / {Scope}";
    }
}

public interface IStorySimulationService
{
    /// <summary><paramref name="options" /> apply to a galactic session and every battle entered from it; a battle started on its own takes them itself.</summary>
    (StorySimStateDto? State, string? Error) Start(StorySimKey key, StorySimOptions? options = null);

    (StorySimStateDto? State, string? Error) Stop(StorySimKey key);
    (StorySimStateDto? State, string? Error) GetState(StorySimKey key, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) SatisfyTrigger(StorySimKey key, string nodeId, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) SetFlag(StorySimKey key, string flag, int value, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) AdvanceClock(StorySimKey key, double seconds, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) LuaNotify(StorySimKey key, string id, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) Tick(StorySimKey key, int count, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) RunToDecision(StorySimKey key, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) Seek(StorySimKey key, int tick);

    (StorySimStateDto? State, string? Error) SetBreakpoints(StorySimKey key, IReadOnlyList<string> nodeIds,
        bool onConditionalGates);

    (StorySimStateDto? State, string? Error) ApplyWorldChange(StorySimKey key, StorySimWorldChangeDto change,
        int sinceSeq = 0);

    /// <summary>
    ///     Resolves a battle of <paramref name="key" />'s campaign faction: its own outcome listeners
    ///     fire (on an implicit session from the galaxy's state when the battle was never entered),
    ///     its flag writes and the author's <paramref name="flags" /> merge into the galactic table,
    ///     its session closes on the outcome, and the galaxy takes the outcome as one command.
    ///     Answers with the state of <paramref name="key" />'s own scope, which is whichever panel asked.
    /// </summary>
    (StorySimStateDto? State, string? Error) ResolveBattle(StorySimKey key, string battleKey, bool won,
        int sinceSeq = 0, IReadOnlyList<StorySimFlagDto>? flags = null);
}

/// <summary>
///     One simulation session per campaign faction and scope, pinned to the model at Start. The
///     session keeps the command log alongside the snapshot: the simulator is deterministic, so
///     seeking to an earlier tick is a replay of the log up to that tick, which needs no per-tick
///     snapshots and drops the future for free.
///     <para>
///         A battle is a session of its own with its own clock, as in the game, which freezes the
///         galaxy while a battle plays: while a battle session is up and unresolved its galactic
///         session refuses every command. The battle starts with the galaxy's flags and world;
///         when it resolves, the flags it wrote and the outcome land in the galactic log as ONE
///         command, so a galactic seek replays the outcome without replaying the battle.
///     </para>
/// </summary>
public sealed class StorySimulationService(
    IStoryModelService modelService,
    IGameIndexService indexService,
    ISchemaProvider schema,
    Action<StorySimKey> notifyChanged,
    IStoryWorldSymbols? symbols = null) : IStorySimulationService
{
    private const int LogTail = 200;
    private const string Won = "won";
    private const string Lost = "lost";
    private readonly object _gate = new();
    private readonly Dictionary<StorySimKey, Session> _sessions = new();

    public (StorySimStateDto? State, string? Error) Start(StorySimKey key, StorySimOptions? options = null)
    {
        var model = modelService.GetCampaignModel(key.Campaign, key.Faction);
        if (model is null)
            return (null, $"{key.Model} was not found.");

        var toNotify = new List<StorySimKey> { key };
        Session session;
        lock (_gate)
        {
            if (key.IsGalactic)
            {
                // A fresh galaxy has no battles in progress: whatever was up belongs to the old run.
                foreach (var battleKey in _sessions.Keys.Where(k => k.Model == key.Model && !k.IsGalactic).ToList())
                {
                    _sessions.Remove(battleKey);
                    toNotify.Add(battleKey);
                }

                var simulator = new StorySimulator(model, schema, symbols, null, options);
                session = new Session(key, simulator, simulator.Start(), CollectLuaNotifications(model.LuaScripts),
                    ImmutableList<SimCommand>.Empty, StorySimBreakpoints.None, model.LuaMachines, model.Battles)
                {
                    // What each battle could write, for the portal's picks: read once, the model is pinned.
                    BattleWrites = model.Battles.ToDictionary(b => b.Key,
                        b => StorySimulator.FlagWritesOf(model, schema, b.Key)
                            .Select(w => new StorySimFlagDto(w.Flag, w.Value)).ToList(),
                        StringComparer.Ordinal)
                };
            }
            else
            {
                var battle = model.Battles.FirstOrDefault(b => b.Key == key.Scope);
                if (battle is null)
                    return (null, $"'{key.Scope}' is not a battle of {key.Model}.");
                if (RunningBattle(key.Model, key) is { } running)
                    return (null, $"Battle '{running.Label}' is still running - resolve it first.");

                // The battle starts where the galaxy stands: its flags and its world, as the game
                // hands a tactical mission the campaign state, and under the galaxy's options.
                // Without a galactic session it runs on its own from the campaign seed.
                _sessions.TryGetValue(key.Galactic, out var galactic);
                var simulator = new StorySimulator(model, schema, symbols, key.Scope,
                    galactic?.Simulator.Options ?? options);
                var seedFlags = galactic?.Snapshot.Runtime.Flags;
                var seedWorld = galactic?.Snapshot.Runtime.World;
                session = new Session(key, simulator,
                    simulator.Start(seedFlags, seedWorld),
                    CollectLuaNotifications(model.LuaScripts), ImmutableList<SimCommand>.Empty,
                    StorySimBreakpoints.None, model.LuaMachines, model.Battles)
                {
                    Label = battle.Label,
                    SeedFlags = seedFlags ?? StoryRuntimeState.Initial.Flags,
                    SeedWorld = seedWorld
                };
                if (galactic is not null) toNotify.Add(key.Galactic);
            }

            _sessions[key] = session;
        }

        foreach (var changed in toNotify) notifyChanged(changed);
        return (ToDto(session, 0), null);
    }

    public (StorySimStateDto? State, string? Error) Stop(StorySimKey key)
    {
        var toNotify = new List<StorySimKey> { key };
        lock (_gate)
        {
            _sessions.Remove(key);
            if (key.IsGalactic)
                // The battles of a stopped galaxy have nothing to resolve into.
                foreach (var battleKey in _sessions.Keys.Where(k => k.Model == key.Model && !k.IsGalactic).ToList())
                {
                    _sessions.Remove(battleKey);
                    toNotify.Add(battleKey);
                }
            else if (_sessions.ContainsKey(key.Galactic))
                // Abandoning a battle un-pauses the galaxy.
                toNotify.Add(key.Galactic);
        }

        foreach (var changed in toNotify) notifyChanged(changed);
        return (StorySimStateDto.NotRunning, null);
    }

    public (StorySimStateDto? State, string? Error) GetState(StorySimKey key, int sinceSeq = 0)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(key, out var session))
                return (ToDto(session, sinceSeq), null);
        }

        return (StorySimStateDto.NotRunning, null);
    }

    public (StorySimStateDto? State, string? Error) SatisfyTrigger(StorySimKey key, string nodeId, int sinceSeq = 0)
    {
        return Mutate(key, new SimCommand(SimCommandKind.Satisfy, nodeId), sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) SetFlag(StorySimKey key, string flag, int value, int sinceSeq = 0)
    {
        return Mutate(key, new SimCommand(SimCommandKind.Flag, flag, value), sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) AdvanceClock(StorySimKey key, double seconds, int sinceSeq = 0)
    {
        var ticks = (int)Math.Round(seconds / StorySimulator.ClockStepSeconds);
        return ticks <= 0 ? GetState(key, sinceSeq) : Tick(key, ticks, sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) LuaNotify(StorySimKey key, string id, int sinceSeq = 0)
    {
        return Mutate(key, new SimCommand(SimCommandKind.Lua, id), sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) Tick(StorySimKey key, int count, int sinceSeq = 0)
    {
        return Mutate(key, new SimCommand(SimCommandKind.Tick, null, count), sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) RunToDecision(StorySimKey key, int sinceSeq = 0)
    {
        return Mutate(key, new SimCommand(SimCommandKind.Run), sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) ApplyWorldChange(StorySimKey key, StorySimWorldChangeDto change,
        int sinceSeq = 0)
    {
        // Inside a battle, the battle's outcome IS its resolution: the in-battle listeners fire and
        // the galaxy takes the result, rather than the battle running on past its own end.
        if (!key.IsGalactic && change.Kind is StoryWorldChangeKind.BattleWon or StoryWorldChangeKind.BattleLost)
            return ResolveBattle(key, key.Scope!, change.Kind == StoryWorldChangeKind.BattleWon, sinceSeq);

        return Mutate(key, new SimCommand(SimCommandKind.World) { Change = ToChange(change) }, sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) Seek(StorySimKey key, int tick)
    {
        Session next;
        var toNotify = new List<StorySimKey> { key };
        lock (_gate)
        {
            if (!_sessions.TryGetValue(key, out var session))
                return (null, $"No simulation is running for {key}.");
            if (Refusal(session) is { } refusal)
                return (null, refusal);
            if (tick < 0 || tick > session.Snapshot.Tick)
                return (null, $"Tick {tick} is outside the run (0 to {session.Snapshot.Tick}).");
            next = Replay(session, tick);
            _sessions[key] = next;

            // A resolution the cut dropped never happened: its battle session goes with it, or
            // the portal would say "won" over a galaxy that has not taken the outcome.
            if (key.IsGalactic)
            {
                var kept = ResolvedBattles(next).Keys.ToHashSet(StringComparer.Ordinal);
                foreach (var stale in _sessions
                             .Where(kvp => kvp.Key.Model == key.Model && !kvp.Key.IsGalactic
                                                                      && kvp.Value.Outcome is not null &&
                                                                      !kept.Contains(kvp.Key.Scope!))
                             .Select(kvp => kvp.Key).ToList())
                {
                    _sessions.Remove(stale);
                    toNotify.Add(stale);
                }
            }
        }

        foreach (var changed in toNotify) notifyChanged(changed);
        return (ToDto(next, 0), null);
    }

    public (StorySimStateDto? State, string? Error) SetBreakpoints(StorySimKey key, IReadOnlyList<string> nodeIds,
        bool onConditionalGates)
    {
        Session next;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(key, out var session))
                return (null, $"No simulation is running for {key}.");
            next = session with
            {
                Breakpoints = new StorySimBreakpoints(
                    nodeIds.ToImmutableHashSet(StringComparer.Ordinal), onConditionalGates)
            };
            _sessions[key] = next;
        }

        notifyChanged(key);
        return (ToDto(next, next.Snapshot.Steps.Count), null);
    }

    public (StorySimStateDto? State, string? Error) ResolveBattle(StorySimKey key, string battleKey, bool won,
        int sinceSeq = 0, IReadOnlyList<StorySimFlagDto>? flags = null)
    {
        var battleSession = new StorySimKey(key.Campaign, key.Faction, battleKey);
        var toNotify = new List<StorySimKey>();
        Session? answer;
        lock (_gate)
        {
            _sessions.TryGetValue(key.Galactic, out var galactic);
            _sessions.TryGetValue(battleSession, out var battle);
            if (galactic is null && battle is null)
                return (null, $"No simulation is running for {key.Model}.");
            if (battle?.Outcome is not null)
                return (null, $"Battle '{battle.Label}' is already {battle.Outcome}.");
            var model = modelService.GetCampaignModel(key.Campaign, key.Faction);
            var label = model?.Battles.FirstOrDefault(b => b.Key == battleSession.Scope)?.Label;
            if (label is null)
                return (null, $"'{battleKey}' is not a battle of {key.Model}.");

            var kind = won ? StoryWorldChangeKind.BattleWon : StoryWorldChangeKind.BattleLost;
            var faction = model!.Faction;
            var outcome = new SimCommand(SimCommandKind.World)
                { Change = new StoryWorldChange(kind) { Faction = faction } };
            // The battle ends on its outcome: its own listeners fire, and what it wrote to the
            // flag table since it started is what crosses back. A battle decided on the portal was
            // never entered, so its listeners run on an implicit session from the galaxy's state -
            // the tutorial's victory listener increments the flag the galaxy reads, played or not.
            StorySimSnapshot ended;
            ImmutableDictionary<string, int> seed;
            if (battle is not null)
            {
                ended = Apply(battle.Simulator, battle.Snapshot, outcome, battle.Breakpoints);
                seed = battle.SeedFlags;
                battle = battle with
                {
                    Snapshot = ended, Commands = battle.Commands.Add(outcome), Outcome = won ? Won : Lost
                };
                _sessions[battleSession] = battle;
                toNotify.Add(battleSession);
            }
            else
            {
                var implicitSim = new StorySimulator(model, schema, symbols, battleSession.Scope,
                    galactic?.Simulator.Options);
                seed = galactic?.Snapshot.Runtime.Flags ?? StoryRuntimeState.Initial.Flags;
                ended = Apply(implicitSim, implicitSim.Start(seed, galactic?.Snapshot.Runtime.World), outcome,
                    StorySimBreakpoints.None);
            }

            var writes = new List<StoryFlagWrite>();
            foreach (var (flag, value) in ended.Runtime.Flags)
                if (!seed.TryGetValue(flag, out var before) || before != value)
                    writes.Add(new StoryFlagWrite(flag, value));
            // The author's picks: what the battle could have set but nothing decided on its own.
            foreach (var pick in flags ?? [])
            {
                writes.RemoveAll(w => w.Flag.Equals(pick.Name, StringComparison.OrdinalIgnoreCase));
                writes.Add(new StoryFlagWrite(pick.Name, pick.Value));
            }

            if (galactic is not null)
            {
                // One command in the galactic log, whatever happened inside the battle.
                var command = new SimCommand(SimCommandKind.Battle, battleSession.Scope, won ? 1 : 0)
                {
                    Change = new StoryWorldChange(kind)
                        { Faction = faction, Flags = writes, BattleKey = battleSession.Scope }
                };
                galactic = galactic with
                {
                    Snapshot = Apply(galactic.Simulator, galactic.Snapshot, command, galactic.Breakpoints),
                    Commands = galactic.Commands.Add(command)
                };
                _sessions[key.Galactic] = galactic;
                toNotify.Add(key.Galactic);
            }

            answer = key.IsGalactic ? galactic : _sessions.GetValueOrDefault(key);
        }

        foreach (var changed in toNotify) notifyChanged(changed);
        return answer is null ? (StorySimStateDto.NotRunning, null) : (ToDto(answer, sinceSeq), null);
    }

    private (StorySimStateDto? State, string? Error) Mutate(StorySimKey key, SimCommand command, int sinceSeq)
    {
        Session next;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(key, out var session))
                return (null, $"No simulation is running for {key}.");
            if (Refusal(session) is { } refusal)
                return (null, refusal);
            next = session with
            {
                Snapshot = Apply(session.Simulator, session.Snapshot, command, session.Breakpoints),
                Commands = session.Commands.Add(command)
            };
            _sessions[key] = next;
        }

        notifyChanged(key);
        return (ToDto(next, sinceSeq), null);
    }

    /// <summary>Why a session takes no command right now, or null. Called under the gate.</summary>
    private string? Refusal(Session session)
    {
        if (session.Outcome is not null)
            return $"Battle '{session.Label}' is {session.Outcome} - start it again to run it once more.";
        if (session.Key.IsGalactic && RunningBattle(session.Key.Model, null) is { } running)
            return $"The galaxy is paused while battle '{running.Label}' runs.";
        return null;
    }

    /// <summary>The unresolved battle session of a campaign faction other than <paramref name="except" />, if any. Called under the gate.</summary>
    private Session? RunningBattle(StoryModelKey model, StorySimKey? except)
    {
        return _sessions.Values.FirstOrDefault(s =>
            s.Key.Model == model && !s.Key.IsGalactic && s.Outcome is null && s.Key != except);
    }

    private static StorySimSnapshot Apply(StorySimulator sim, StorySimSnapshot snapshot, SimCommand command,
        StorySimBreakpoints breakpoints)
    {
        return command.Kind switch
        {
            SimCommandKind.Satisfy => sim.SatisfyTrigger(snapshot, command.Text!),
            SimCommandKind.Flag => sim.SetFlag(snapshot, command.Text!, command.Number),
            SimCommandKind.Lua => sim.LuaNotify(snapshot, command.Text!),
            SimCommandKind.Tick => sim.Tick(snapshot, command.Number, breakpoints),
            SimCommandKind.Run => sim.RunToDecision(snapshot, breakpoints),
            SimCommandKind.World when command.Change is { } change => sim.ApplyWorldChange(snapshot, change),
            SimCommandKind.Battle when command.Change is { } change => sim.ResolveBattleOutcome(snapshot, change),
            _ => snapshot
        };
    }

    /// <summary>
    ///     Replays the command log from Start until the run first reaches <paramref name="tick" />,
    ///     ticking one at a time inside tick and run commands so the stop lands exactly. The log is
    ///     cut there: a tick command is shortened to the ticks consumed, a run becomes those ticks.
    /// </summary>
    private static Session Replay(Session session, int tick)
    {
        var sim = session.Simulator;
        var snapshot = sim.Start(session.Key.IsGalactic ? null : session.SeedFlags, session.SeedWorld);
        var commands = ImmutableList<SimCommand>.Empty;
        foreach (var command in session.Commands)
        {
            if (snapshot.Tick >= tick) break;
            if (command.Kind is SimCommandKind.Tick or SimCommandKind.Run)
            {
                var budget = command.Kind == SimCommandKind.Tick ? command.Number : int.MaxValue;
                var consumed = 0;
                while (consumed < budget && snapshot.Tick < tick)
                {
                    snapshot = sim.Tick(snapshot);
                    consumed++;
                }

                if (consumed > 0) commands = commands.Add(new SimCommand(SimCommandKind.Tick, null, consumed));
                continue;
            }

            snapshot = Apply(sim, snapshot, command, StorySimBreakpoints.None);
            commands = commands.Add(command);
        }

        return session with { Snapshot = snapshot, Commands = commands };
    }

    /// <summary>The battles a galactic log has resolved, last word per battle. </summary>
    private static Dictionary<string, string> ResolvedBattles(Session galactic)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var command in galactic.Commands)
            if (command.Kind == SimCommandKind.Battle && command.Text is { } battleKey)
                resolved[battleKey] = command.Number == 1 ? Won : Lost;
        return resolved;
    }

    /// <summary>Builds the state document. Called under the gate: the battle list reads the other sessions.</summary>
    private StorySimStateDto ToDto(Session session, int sinceSeq)
    {
        var snapshot = session.Snapshot;
        var fireCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var step in snapshot.Steps)
            if (StorySimCause.Fires.Contains(step.Cause) && step.To == StoryEventLifecycle.Fired)
                fireCounts[step.NodeId] = fireCounts.GetValueOrDefault(step.NodeId) + 1;

        string? pausedFor = null;
        var battles = new List<StorySimBattleDto>();
        if (session.Key.IsGalactic)
            lock (_gate)
            {
                pausedFor = RunningBattle(session.Key.Model, null)?.Label;
                var resolved = ResolvedBattles(session);
                foreach (var battle in session.Battles)
                {
                    var running = _sessions.GetValueOrDefault(session.Key with { Scope = battle.Key });
                    var status = resolved.GetValueOrDefault(battle.Key)
                                 ?? (running is { Outcome: null } ? "running" : "notStarted");
                    battles.Add(new StorySimBattleDto(battle.Key, battle.Label, status, running?.Snapshot.Tick ?? 0,
                        session.BattleWrites.GetValueOrDefault(battle.Key) ?? []));
                }
            }

        return new StorySimStateDto(
            true,
            snapshot.Tick,
            snapshot.Clock,
            StorySimulator.ClockStepSeconds,
            snapshot.Runtime.Flags
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => new StorySimFlagDto(kvp.Key, kvp.Value))
                .ToList(),
            session.Simulator.GetLifecycles(snapshot)
                .Select(kvp =>
                {
                    var gate = session.Simulator.GetGate(snapshot, kvp.Key);
                    return new StorySimNodeStateDto(kvp.Key, kvp.Value.ToString(),
                        fireCounts.GetValueOrDefault(kvp.Key), gate?.Label, gate?.Progress);
                })
                .ToList(),
            session.Simulator.GetInterventions(snapshot)
                .Select(i => new StorySimInterventionDto(i.Kind, i.NodeId, i.EventName, i.EventType, i.Options,
                    i.Facet, i.Suggested is null ? null : ToChangeDto(i.Suggested), i.BattleKey))
                .ToList(),
            session.LuaNotifications,
            snapshot.Log.Count > LogTail ? snapshot.Log.GetRange(snapshot.Log.Count - LogTail, LogTail) : snapshot.Log,
            snapshot.Steps
                .Where(s => s.Seq >= Math.Max(0, sinceSeq))
                .Select(s => new StorySimStepDto(s.Tick, s.Seq, s.NodeId, s.From?.ToString(), s.To?.ToString(),
                    s.SourceNodeId, s.Cause, s.Detail))
                .ToList(),
            snapshot.Steps.Count,
            session.Breakpoints.NodeIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            session.Breakpoints.OnConditionalGates,
            snapshot.HaltedAt,
            ToWorldDto(snapshot.Runtime.World),
            ToLuaStates(session, snapshot),
            session.Simulator.GetClockPending(snapshot),
            session.Key.Scope,
            pausedFor,
            session.Outcome,
            battles,
            session.Simulator.Options.AssumeMediaCompletes);
    }

    private static List<StorySimLuaStateDto> ToLuaStates(Session session, StorySimSnapshot snapshot)
    {
        return session.Machines.Select(machine =>
        {
            var state = snapshot.Runtime.Scripts.GetValueOrDefault(machine.ScriptUri);
            var pending = snapshot.Runtime.PendingEmissions
                .Where(p => p.ScriptUri.Equals(machine.ScriptUri, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.DueClock)
                .Select(p => new StorySimLuaPendingDto(p.Id, p.State, p.DueClock))
                .ToList();
            return new StorySimLuaStateDto(machine.ScriptUri, machine.ScriptName, state?.Current, state?.Next, pending);
        }).ToList();
    }

    private static StorySimWorldDto ToWorldDto(StoryWorld world)
    {
        static List<StorySimFlagDto> Pairs(IEnumerable<KeyValuePair<string, int>> source)
        {
            return source.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => new StorySimFlagDto(kvp.Key, kvp.Value)).ToList();
        }

        return new StorySimWorldDto(
            world.Planets.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => new StorySimPlanetDto(p.Name, p.Owner, p.Revealed, p.Corrupted, p.Destroyed)).ToList(),
            world.UnitFacts().OrderBy(u => u.Planet, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.Owner, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.Type, StringComparer.OrdinalIgnoreCase)
                .Select(u => new StorySimUnitDto(u.Type, u.Owner, u.Planet, u.Count)).ToList(),
            Pairs(world.Tech), Pairs(world.Credits), world.Era, Pairs(world.Counters),
            world.Objectives.OrderBy(o => o, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static StoryWorldChange ToChange(StorySimWorldChangeDto dto)
    {
        return new StoryWorldChange(dto.Kind)
        {
            Planet = dto.Planet, UnitType = dto.UnitType, Faction = dto.Faction, Name = dto.Name, Mode = dto.Mode,
            Amount = dto.Amount, NodeId = dto.NodeId,
            Flags = dto.Flags?.Select(f => new StoryFlagWrite(f.Name, f.Value)).ToList()
        };
    }

    private static StorySimWorldChangeDto ToChangeDto(StoryWorldChange change)
    {
        return new StorySimWorldChangeDto(change.Kind)
        {
            Planet = change.Planet, UnitType = change.UnitType, Faction = change.Faction, Name = change.Name,
            Mode = change.Mode, Amount = change.Amount, NodeId = change.NodeId,
            Flags = change.Flags?.Select(f => new StorySimFlagDto(f.Flag, f.Value)).ToList()
        };
    }

    private IReadOnlyList<string> CollectLuaNotifications(IReadOnlyList<string> luaScripts)
    {
        if (luaScripts.Count == 0) return [];
        var suffixes = luaScripts
            .Select(s =>
                "/" + s.ToLowerInvariant() + (s.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? "" : ".lua"))
            .ToList();

        var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definitions in indexService.Current.WorkspaceDefinitions.Values)
        foreach (var symbol in definitions)
        {
            if (symbol.TypeName != StoryReferenceTypes.NotificationSymbol) continue;
            if (symbol.Origin is not FileOrigin origin) continue;
            if (suffixes.Any(s => origin.Uri.EndsWith(s, StringComparison.Ordinal)))
                ids.Add(symbol.Id);
        }

        return ids.ToList();
    }

    private enum SimCommandKind
    {
        Satisfy,
        Flag,
        Lua,
        Tick,
        Run,
        World,

        /// <summary>A battle's resolution in the galactic log: Text is the battle key, Number 1 for won.</summary>
        Battle
    }

    private sealed record SimCommand(SimCommandKind Kind, string? Text = null, int Number = 0)
    {
        public StoryWorldChange? Change { get; init; }
    }

    /// <summary>
    ///     <see cref="SeedFlags" /> and <see cref="SeedWorld" /> are what a battle started from, so
    ///     a battle seek replays from the same footing and a resolution knows which flags the
    ///     battle wrote; <see cref="Outcome" /> is set once it has resolved.
    /// </summary>
    private sealed record Session(
        StorySimKey Key,
        StorySimulator Simulator,
        StorySimSnapshot Snapshot,
        IReadOnlyList<string> LuaNotifications,
        ImmutableList<SimCommand> Commands,
        StorySimBreakpoints Breakpoints,
        IReadOnlyList<LuaStoryMachine> Machines,
        IReadOnlyList<StoryBattle> Battles)
    {
        public string? Label { get; init; }

        /// <summary>Galactic only: per battle, the flags its rewards can write - the portal's picks.</summary>
        public IReadOnlyDictionary<string, List<StorySimFlagDto>> BattleWrites { get; init; } =
            new Dictionary<string, List<StorySimFlagDto>>(StringComparer.Ordinal);

        public ImmutableDictionary<string, int> SeedFlags { get; init; } = StoryRuntimeState.Initial.Flags;
        public StoryWorld? SeedWorld { get; init; }
        public string? Outcome { get; init; }
    }
}