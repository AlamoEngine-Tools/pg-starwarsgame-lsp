// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Server.Story;

public interface IStorySimulationService
{
    (StorySimStateDto? State, string? Error) Start(string campaign);
    (StorySimStateDto? State, string? Error) Stop(string campaign);
    (StorySimStateDto? State, string? Error) GetState(string campaign);
    (StorySimStateDto? State, string? Error) SatisfyTrigger(string campaign, string nodeId);
    (StorySimStateDto? State, string? Error) SetFlag(string campaign, string flag, int value);
    (StorySimStateDto? State, string? Error) AdvanceClock(string campaign, double seconds);
    (StorySimStateDto? State, string? Error) LuaNotify(string campaign, string id);
}

/// <summary>
///     One simulation session per campaign, pinned to the campaign model that existed at
///     <see cref="Start" /> - edits during a run don't mutate a running story; restart to pick
///     them up. Every state change invokes the notify delegate (aet/storySimChanged) so all
///     panels re-fetch. The Lua notification catalogue is collected from the workspace index's
///     StoryNotification symbols, filtered to the campaign's attached scripts.
/// </summary>
public sealed class StorySimulationService(
    IStoryModelService modelService,
    IGameIndexService indexService,
    ISchemaProvider schema,
    Action<string> notifyChanged) : IStorySimulationService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public (StorySimStateDto? State, string? Error) Start(string campaign)
    {
        var model = modelService.GetCampaignModel(campaign);
        if (model is null)
            return (null, $"Campaign '{campaign}' was not found.");

        var simulator = new StorySimulator(model, schema);
        var session = new Session(simulator, simulator.Start(), CollectLuaNotifications(model.LuaScripts));
        lock (_gate)
        {
            _sessions[campaign] = session;
        }

        notifyChanged(campaign);
        return (ToDto(session, true), null);
    }

    public (StorySimStateDto? State, string? Error) Stop(string campaign)
    {
        lock (_gate)
        {
            _sessions.Remove(campaign);
        }

        notifyChanged(campaign);
        return (new StorySimStateDto(false, 0, [], [], [], [], []), null);
    }

    public (StorySimStateDto? State, string? Error) GetState(string campaign)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(campaign, out var session))
                return (ToDto(session, true), null);
        }

        return (new StorySimStateDto(false, 0, [], [], [], [], []), null);
    }

    public (StorySimStateDto? State, string? Error) SatisfyTrigger(string campaign, string nodeId)
    {
        return Mutate(campaign, (sim, snapshot) => sim.SatisfyTrigger(snapshot, nodeId));
    }

    public (StorySimStateDto? State, string? Error) SetFlag(string campaign, string flag, int value)
    {
        return Mutate(campaign, (sim, snapshot) => sim.SetFlag(snapshot, flag, value));
    }

    public (StorySimStateDto? State, string? Error) AdvanceClock(string campaign, double seconds)
    {
        return Mutate(campaign, (sim, snapshot) => sim.AdvanceClock(snapshot, seconds));
    }

    public (StorySimStateDto? State, string? Error) LuaNotify(string campaign, string id)
    {
        return Mutate(campaign, (sim, snapshot) => sim.LuaNotify(snapshot, id));
    }

    private (StorySimStateDto? State, string? Error) Mutate(
        string campaign, Func<StorySimulator, StorySimSnapshot, StorySimSnapshot> step)
    {
        Session next;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(campaign, out var session))
                return (null, $"No simulation is running for campaign '{campaign}'.");
            next = session with { Snapshot = step(session.Simulator, session.Snapshot) };
            _sessions[campaign] = next;
        }

        notifyChanged(campaign);
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