// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server.Story;

public sealed class GetStoryPlotsHandler(
    IStoryModelService modelService,
    IGameIndexService indexService,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetStoryPlotsParams, GetStoryPlotsResult>
{
    public Task<GetStoryPlotsResult> Handle(GetStoryPlotsParams request, CancellationToken ct)
    {
        if (StoryEditorFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new GetStoryPlotsResult([], rejection));

        var chain = modelService.GetChainResult();
        var manifestsByFile = new Dictionary<string, StoryManifestContents>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in chain.Manifests)
            manifestsByFile.TryAdd(manifest.ManifestFile, manifest);

        var campaigns = new List<StoryCampaignDto>();
        foreach (var campaign in chain.Campaigns)
        {
            var model = modelService.GetCampaignModel(campaign.Name);
            // Manifest entries use engine casing; canonical thread URIs are lowercase.
            string? ResolveUri(string thread)
            {
                return model?.Threads.FirstOrDefault(t => t.DocumentUri.EndsWith(
                    "/" + thread.ToLowerInvariant(), StringComparison.Ordinal))?.DocumentUri;
            }

            var factions = new List<StoryFactionDto>();
            foreach (var faction in campaign.FactionManifests)
            {
                manifestsByFile.TryGetValue(faction.ManifestFile, out var contents);
                var threads = new List<StoryPlotThreadDto>();
                foreach (var thread in contents?.ActiveThreads ?? [])
                    threads.Add(new StoryPlotThreadDto(thread, Suspended: false, ResolveUri(thread)));
                foreach (var thread in contents?.SuspendedThreads ?? [])
                {
                    var uri = ResolveUri(thread);
                    threads.Add(new StoryPlotThreadDto(thread,
                        Suspended: uri is null || model!.SuspendedThreadUris.Contains(uri), uri));
                }

                var luaScripts = new List<StoryLuaScriptDto>();
                foreach (var script in contents?.LuaScripts ?? [])
                    luaScripts.Add(new StoryLuaScriptDto(script, ResolveLuaUri(script)));
                factions.Add(new StoryFactionDto(faction.Faction, faction.ManifestFile,
                    threads, luaScripts));
            }

            campaigns.Add(new StoryCampaignDto(campaign.Name, factions));
        }

        return Task.FromResult(new GetStoryPlotsResult(campaigns));
    }

    // Manifest Lua_Script entries are extensionless engine-cased names; indexed lua documents
    // are keyed by lowercase canonical URI.
    private string? ResolveLuaUri(string script)
    {
        var fileName = script.ToLowerInvariant();
        if (!fileName.EndsWith(".lua", StringComparison.Ordinal)) fileName += ".lua";
        var suffix = "/" + fileName;
        return indexService.Current.Documents.Keys
            .FirstOrDefault(uri => uri.EndsWith(suffix, StringComparison.Ordinal));
    }
}

public sealed class GetStoryGraphHandler(IStoryModelService modelService, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetStoryGraphParams, GetStoryGraphResult>
{
    public Task<GetStoryGraphResult> Handle(GetStoryGraphParams request, CancellationToken ct)
    {
        if (StoryEditorFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new GetStoryGraphResult([], [], rejection));

        var model = modelService.GetCampaignModel(request.Campaign);
        if (model is null)
            return Task.FromResult(new GetStoryGraphResult([], [],
                $"Campaign '{request.Campaign}' was not found."));

        var evaluator = new StoryEvaluator(model.Graph);
        var state = StoryRuntimeState.Initial with
        {
            SuspendedThreads = StoryRuntimeState.Initial.SuspendedThreads.Union(model.SuspendedThreadUris)
        };
        var reachable = evaluator.ComputeReachableEvents();

        // Filters select EVENT nodes; virtual nodes and edges survive when both endpoints do.
        var keptEvents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in model.Graph.Nodes.Where(n => n.Kind == StoryNodeKind.Event))
        {
            if (request.NameFilter is { Length: > 0 } name &&
                !node.Label.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (request.Branch is { Length: > 0 } branch &&
                !string.Equals(node.Event!.Branch, branch, StringComparison.OrdinalIgnoreCase)) continue;
            if (request.Lifecycle is { Length: > 0 } lifecycle &&
                !string.Equals(evaluator.GetLifecycle(node.Id, state).ToString(), lifecycle,
                    StringComparison.OrdinalIgnoreCase)) continue;
            keptEvents.Add(node.Id);
        }

        if (request.ReachableFrom is { Length: > 0 } fromId)
            keptEvents.IntersectWith(ForwardClosure(model.Graph, fromId));

        var keptNodes = new List<StoryGraphNodeDto>();
        var keptIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in model.Graph.Nodes)
        {
            var keep = node.Kind == StoryNodeKind.Event
                ? keptEvents.Contains(node.Id)
                : model.Graph.Edges.Any(e =>
                    (e.FromId == node.Id && keptEvents.Contains(e.ToId)) ||
                    (e.ToId == node.Id && keptEvents.Contains(e.FromId)));
            if (!keep) continue;

            keptIds.Add(node.Id);
            keptNodes.Add(new StoryGraphNodeDto(
                node.Id, node.Kind.ToString(), node.Label, node.ThreadUri,
                node.Event?.NameRange.StartLine,
                node.Event?.EventType, node.Event?.RewardType, node.Event?.Branch,
                node.Kind == StoryNodeKind.Event
                    ? evaluator.GetLifecycle(node.Id, state).ToString()
                    : null,
                node.Kind != StoryNodeKind.Event || reachable.Contains(node.Id)));
        }

        var edges = model.Graph.Edges
            .Where(e => keptIds.Contains(e.FromId) && keptIds.Contains(e.ToId))
            .Select(e => new StoryGraphEdgeDto(e.FromId, e.ToId, e.Kind.ToString(), e.Label))
            .ToList();

        return Task.FromResult(new GetStoryGraphResult(keptNodes, edges));
    }

    // Everything transitively downstream of the given node — "show me what this event leads to".
    private static HashSet<string> ForwardClosure(StoryGraph graph, string fromId)
    {
        var byFrom = graph.Edges.ToLookup(e => e.FromId, StringComparer.Ordinal);
        var closure = new HashSet<string>(StringComparer.Ordinal) { fromId };
        var queue = new Queue<string>([fromId]);
        while (queue.Count > 0)
            foreach (var edge in byFrom[queue.Dequeue()])
                if (closure.Add(edge.ToId))
                    queue.Enqueue(edge.ToId);
        return closure;
    }
}

public sealed class GetStoryNodeDetailHandler(IStoryModelService modelService, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetStoryNodeDetailParams, GetStoryNodeDetailResult>
{
    public Task<GetStoryNodeDetailResult> Handle(GetStoryNodeDetailParams request, CancellationToken ct)
    {
        if (StoryEditorFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new GetStoryNodeDetailResult(null, rejection));

        var model = modelService.GetCampaignModel(request.Campaign);
        var node = model?.Graph.Nodes.FirstOrDefault(n =>
            n.Kind == StoryNodeKind.Event && n.Id == request.NodeId);
        if (node?.Event is not { } storyEvent)
            return Task.FromResult(new GetStoryNodeDetailResult(null,
                $"Node '{request.NodeId}' was not found in campaign '{request.Campaign}'."));

        return Task.FromResult(new GetStoryNodeDetailResult(new StoryNodeDetailDto(
            node.Id,
            storyEvent.Name,
            node.ThreadUri,
            storyEvent.Range.StartLine,
            storyEvent.EventType,
            storyEvent.EventFilter,
            storyEvent.EventParams.Select(p => new StoryParamValueDto(p.Position, p.RawValue)).ToList(),
            storyEvent.RewardType,
            storyEvent.RewardParams.Select(p => new StoryParamValueDto(p.Position, p.RawValue)).ToList(),
            storyEvent.PrereqGroups
                .Select(g => (IReadOnlyList<string>)g.Tokens.Select(t => t.Text).ToList())
                .ToList(),
            storyEvent.Branch,
            storyEvent.Perpetual,
            storyEvent.StoryDialog,
            storyEvent.StoryChapter,
            storyEvent.Tags.Select(t => new StoryTagDto(t.Name, t.Value)).ToList())));
    }
}

public sealed class GetStoryLayoutHandler(IStoryLayoutStore store, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetStoryLayoutParams, GetStoryLayoutResult>
{
    public Task<GetStoryLayoutResult> Handle(GetStoryLayoutParams request, CancellationToken ct)
    {
        if (StoryEditorFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new GetStoryLayoutResult([], rejection));

        var entries = store.Get(request.Campaign)
            .Select(e => new StoryLayoutEntryDto(e.File, e.EventName, e.X, e.Y))
            .ToList();
        return Task.FromResult(new GetStoryLayoutResult(entries));
    }
}

public sealed class SetStoryLayoutHandler(IStoryLayoutStore store, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<SetStoryLayoutParams, SetStoryLayoutResult>
{
    public Task<SetStoryLayoutResult> Handle(SetStoryLayoutParams request, CancellationToken ct)
    {
        if (StoryEditorFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new SetStoryLayoutResult(false, rejection));

        store.Set(request.Campaign, request.Entries
            .Select(e => new StoryLayoutEntry(e.File, e.EventName, e.X, e.Y))
            .ToList());
        return Task.FromResult(new SetStoryLayoutResult(true));
    }
}

public sealed class GetStorySchemaHandler(ISchemaProvider schema, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetStorySchemaParams, GetStorySchemaResult>
{
    public Task<GetStorySchemaResult> Handle(GetStorySchemaParams request, CancellationToken ct)
    {
        if (StoryEditorFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new GetStorySchemaResult([], [], rejection));

        return Task.FromResult(new GetStorySchemaResult(
            Project(schema.GetEnum("StoryEventType")),
            Project(schema.GetEnum("StoryRewardType"))));
    }

    private static IReadOnlyList<StoryTypeSchemaDto> Project(EnumDefinition? definition)
    {
        var types = new List<StoryTypeSchemaDto>();
        foreach (var value in definition?.Values ?? [])
            types.Add(new StoryTypeSchemaDto(
                value.Name,
                value.Description.GetValueOrDefault("en"),
                value.Untested,
                (value.Params ?? (IReadOnlyList<ParamDefinition>)[])
                .Select(p => new StoryParamSchemaDto(
                    p.Position,
                    p.ValueType.ToString(),
                    p.ReferenceTypeName ?? p.ObjectType?.TypeName,
                    p.Enum?.Name,
                    p.Optional,
                    p.Description.GetValueOrDefault("en")))
                .ToList()));
        return types;
    }
}
