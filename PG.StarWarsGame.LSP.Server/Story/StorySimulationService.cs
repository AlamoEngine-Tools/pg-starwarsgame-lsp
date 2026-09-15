// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Server.Story;

public interface IStorySimulationService
{
    (StorySimStateDto? State, string? Error) Start(StoryModelKey key);
    (StorySimStateDto? State, string? Error) Stop(StoryModelKey key);
    (StorySimStateDto? State, string? Error) GetState(StoryModelKey key);
    (StorySimStateDto? State, string? Error) SatisfyTrigger(StoryModelKey key, string nodeId);
    (StorySimStateDto? State, string? Error) SetFlag(StoryModelKey key, string flag, int value);
    (StorySimStateDto? State, string? Error) AdvanceClock(StoryModelKey key, double seconds);
    (StorySimStateDto? State, string? Error) LuaNotify(StoryModelKey key, string id);
}

/// <summary>
///     One simulation session per campaign FACTION, pinned to the model that existed at
///     <see cref="Start" /> - edits during a run don't mutate a running story; restart to pick
///     them up. Every state change invokes the notify delegate (aet/storySimChanged) so all
///     panels re-fetch. The Lua notification catalogue is collected from the workspace index's
///     StoryNotification symbols, filtered to that faction's attached scripts.
/// </summary>
public sealed class StorySimulationService(
    IStoryModelService modelService,
    IGameIndexService indexService,
    ISchemaProvider schema,
    Action<StoryModelKey> notifyChanged) : IStorySimulationService
{
    private readonly object _gate = new();
    private readonly Dictionary<StoryModelKey, Session> _sessions = new();

    public (StorySimStateDto? State, string? Error) Start(StoryModelKey key)
    {
        var model = modelService.GetCampaignModel(key.Campaign, key.Faction);
        if (model is null)
            return (null, $"{key} was not found.");

        var simulator = new StorySimulator(model, schema);
        var session = new Session(simulator, simulator.Start(), CollectLuaNotifications(model.LuaScripts));
        lock (_gate)
        {
            _sessions[key] = session;
        }

        notifyChanged(key);
        return (ToDto(session, true), null);
    }

    public (StorySimStateDto? State, string? Error) Stop(StoryModelKey key)
    {
        lock (_gate)
        {
            _sessions.Remove(key);
        }

        notifyChanged(key);
        return (new StorySimStateDto(false, 0, [], [], [], [], []), null);
    }

    public (StorySimStateDto? State, string? Error) GetState(StoryModelKey key)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(key, out var session))
                return (ToDto(session, true), null);
        }

        return (new StorySimStateDto(false, 0, [], [], [], [], []), null);
    }

    public (StorySimStateDto? State, string? Error) SatisfyTrigger(StoryModelKey key, string nodeId)
    {
        return Mutate(key, (sim, snapshot) => sim.SatisfyTrigger(snapshot, nodeId));
    }

    public (StorySimStateDto? State, string? Error) SetFlag(StoryModelKey key, string flag, int value)
    {
        return Mutate(key, (sim, snapshot) => sim.SetFlag(snapshot, flag, value));
    }

    public (StorySimStateDto? State, string? Error) AdvanceClock(StoryModelKey key, double seconds)
    {
        return Mutate(key, (sim, snapshot) => sim.AdvanceClock(snapshot, seconds));
    }

    public (StorySimStateDto? State, string? Error) LuaNotify(StoryModelKey key, string id)
    {
        return Mutate(key, (sim, snapshot) => sim.LuaNotify(snapshot, id));
    }

    private (StorySimStateDto? State, string? Error) Mutate(
        StoryModelKey key, Func<StorySimulator, StorySimSnapshot, StorySimSnapshot> step)
    {
        Session next;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(key, out var session))
                return (null, $"No simulation is running for {key}.");
            next = session with { Snapshot = step(session.Simulator, session.Snapshot) };
            _sessions[key] = next;
        }

        notifyChanged(key);
        return (ToDto(next, true), null);
    }

    private static StorySimStateDto ToDto(Session session, bool running)
    {
        var snapshot = session.Snapshot;
        return new StorySimStateDto(
            running,
            snapshot.Clock,
            snapshot.Runtime.Flags
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => new StorySimFlagDto(kvp.Key, kvp.Value))
                .ToList(),
            session.Simulator.GetLifecycles(snapshot)
                .Select(kvp => new StorySimNodeStateDto(kvp.Key, kvp.Value.ToString()))
                .ToList(),
            session.Simulator.GetInterventions(snapshot)
                .Select(i => new StorySimInterventionDto(i.Kind, i.NodeId, i.EventName, i.EventType, i.Options))
                .ToList(),
            session.LuaNotifications,
            snapshot.Log);
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

    private sealed record Session(
        StorySimulator Simulator,
        StorySimSnapshot Snapshot,
        IReadOnlyList<string> LuaNotifications);
}