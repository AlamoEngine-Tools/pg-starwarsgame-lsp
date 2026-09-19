// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Server.Story;

public interface IStorySimulationService
{
    (StorySimStateDto? State, string? Error) Start(StoryModelKey key);
    (StorySimStateDto? State, string? Error) Stop(StoryModelKey key);
    (StorySimStateDto? State, string? Error) GetState(StoryModelKey key, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) SatisfyTrigger(StoryModelKey key, string nodeId, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) SetFlag(StoryModelKey key, string flag, int value, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) AdvanceClock(StoryModelKey key, double seconds, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) LuaNotify(StoryModelKey key, string id, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) Tick(StoryModelKey key, int count, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) RunToDecision(StoryModelKey key, int sinceSeq = 0);
    (StorySimStateDto? State, string? Error) Seek(StoryModelKey key, int tick);

    (StorySimStateDto? State, string? Error) SetBreakpoints(StoryModelKey key, IReadOnlyList<string> nodeIds,
        bool onConditionalGates);

    (StorySimStateDto? State, string? Error) ApplyWorldChange(StoryModelKey key, StorySimWorldChangeDto change,
        int sinceSeq = 0);
}

/// <summary>
///     One simulation session per campaign and faction, pinned to the model at Start. The session
///     keeps the command log alongside the snapshot: the simulator is deterministic, so seeking to
///     an earlier tick is a replay of the log up to that tick, which needs no per-tick snapshots
///     and drops the future for free.
/// </summary>
public sealed class StorySimulationService(
    IStoryModelService modelService,
    IGameIndexService indexService,
    ISchemaProvider schema,
    Action<StoryModelKey> notifyChanged,
    IStoryWorldSymbols? symbols = null) : IStorySimulationService
{
    private const int LogTail = 200;
    private readonly object _gate = new();
    private readonly Dictionary<StoryModelKey, Session> _sessions = new();

    public (StorySimStateDto? State, string? Error) Start(StoryModelKey key)
    {
        var model = modelService.GetCampaignModel(key.Campaign, key.Faction);
        if (model is null)
            return (null, $"{key} was not found.");

        var simulator = new StorySimulator(model, schema, symbols);
        var session = new Session(simulator, simulator.Start(), CollectLuaNotifications(model.LuaScripts),
            ImmutableList<SimCommand>.Empty, StorySimBreakpoints.None, model.LuaMachines);
        lock (_gate)
        {
            _sessions[key] = session;
        }

        notifyChanged(key);
        return (ToDto(session, 0), null);
    }

    public (StorySimStateDto? State, string? Error) Stop(StoryModelKey key)
    {
        lock (_gate)
        {
            _sessions.Remove(key);
        }

        notifyChanged(key);
        return (StorySimStateDto.NotRunning, null);
    }

    public (StorySimStateDto? State, string? Error) GetState(StoryModelKey key, int sinceSeq = 0)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(key, out var session))
                return (ToDto(session, sinceSeq), null);
        }

        return (StorySimStateDto.NotRunning, null);
    }

    public (StorySimStateDto? State, string? Error) SatisfyTrigger(StoryModelKey key, string nodeId, int sinceSeq = 0)
    {
        return Mutate(key, new SimCommand(SimCommandKind.Satisfy, nodeId), sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) SetFlag(StoryModelKey key, string flag, int value, int sinceSeq = 0)
    {
        return Mutate(key, new SimCommand(SimCommandKind.Flag, flag, value), sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) AdvanceClock(StoryModelKey key, double seconds, int sinceSeq = 0)
    {
        var ticks = (int)Math.Round(seconds / StorySimulator.ClockStepSeconds);
        return ticks <= 0 ? GetState(key, sinceSeq) : Tick(key, ticks, sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) LuaNotify(StoryModelKey key, string id, int sinceSeq = 0)
    {
        return Mutate(key, new SimCommand(SimCommandKind.Lua, id), sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) Tick(StoryModelKey key, int count, int sinceSeq = 0)
    {
        return Mutate(key, new SimCommand(SimCommandKind.Tick, null, count), sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) RunToDecision(StoryModelKey key, int sinceSeq = 0)
    {
        return Mutate(key, new SimCommand(SimCommandKind.Run), sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) ApplyWorldChange(StoryModelKey key, StorySimWorldChangeDto change,
        int sinceSeq = 0)
    {
        return Mutate(key, new SimCommand(SimCommandKind.World) { Change = ToChange(change) }, sinceSeq);
    }

    public (StorySimStateDto? State, string? Error) Seek(StoryModelKey key, int tick)
    {
        Session next;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(key, out var session))
                return (null, $"No simulation is running for {key}.");
            if (tick < 0 || tick > session.Snapshot.Tick)
                return (null, $"Tick {tick} is outside the run (0 to {session.Snapshot.Tick}).");
            next = Replay(session, tick);
            _sessions[key] = next;
        }

        notifyChanged(key);
        return (ToDto(next, 0), null);
    }

    public (StorySimStateDto? State, string? Error) SetBreakpoints(StoryModelKey key, IReadOnlyList<string> nodeIds,
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

    private (StorySimStateDto? State, string? Error) Mutate(StoryModelKey key, SimCommand command, int sinceSeq)
    {
        Session next;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(key, out var session))
                return (null, $"No simulation is running for {key}.");
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
        var snapshot = sim.Start();
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

    private static StorySimStateDto ToDto(Session session, int sinceSeq)
    {
        var snapshot = session.Snapshot;
        var fireCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var step in snapshot.Steps)
            if (StorySimCause.Fires.Contains(step.Cause) && step.To == StoryEventLifecycle.Fired)
                fireCounts[step.NodeId] = fireCounts.GetValueOrDefault(step.NodeId) + 1;

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
                    i.Facet, i.Suggested is null ? null : ToChangeDto(i.Suggested)))
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
            ToLuaStates(session, snapshot));
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
        World
    }

    private sealed record SimCommand(SimCommandKind Kind, string? Text = null, int Number = 0)
    {
        public StoryWorldChange? Change { get; init; }
    }

    private sealed record Session(
        StorySimulator Simulator,
        StorySimSnapshot Snapshot,
        IReadOnlyList<string> LuaNotifications,
        ImmutableList<SimCommand> Commands,
        StorySimBreakpoints Breakpoints,
        IReadOnlyList<LuaStoryMachine> Machines);
}