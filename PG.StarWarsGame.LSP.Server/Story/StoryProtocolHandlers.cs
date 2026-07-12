// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Xml;
using PG.StarWarsGame.LSP.Xml.Completion;

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

        return Task.FromResult(StoryGraphProjection.Project(
            model, request.NameFilter, request.Branch, request.Lifecycle, request.ReachableFrom));
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
                    p.Description.GetValueOrDefault("en"),
                    p.Enum?.Values.Select(v => v.Name).ToList()))
                .ToList()));
        return types;
    }
}

public sealed class GetStoryParamOptionsHandler(
    IStoryModelService modelService,
    IGameIndexService indexService,
    ISchemaProvider schema,
    StoryParamValueProposalProvider proposals,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetStoryParamOptionsParams, GetStoryParamOptionsResult>
{
    private const int DefaultLimit = 100;

    public Task<GetStoryParamOptionsResult> Handle(GetStoryParamOptionsParams request, CancellationToken ct)
    {
        if (StoryEditorFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new GetStoryParamOptionsResult([], rejection));

        var enumName = string.Equals(request.Side, "reward", StringComparison.OrdinalIgnoreCase)
            ? "StoryRewardType"
            : "StoryEventType";
        var paramDef = schema.GetEnum(enumName)?.Values
            .FirstOrDefault(v => string.Equals(v.Name, request.TypeName, StringComparison.OrdinalIgnoreCase))
            ?.Params?.FirstOrDefault(p => p.Position == request.Position);
        if (paramDef is null)
            return Task.FromResult(new GetStoryParamOptionsResult([]));

        var prefix = request.Prefix ?? string.Empty;
        var limit = request.Limit is int l and > 0 ? l : DefaultLimit;

        // Story-scoped names resolve campaign-wide — the campaign model gives a far tighter
        // candidate set than the index (which mixes every campaign's names together).
        var campaignScoped = CampaignScopedOptions(paramDef.ReferenceTypeName, request.Campaign, prefix);
        if (campaignScoped is not null)
            return Task.FromResult(new GetStoryParamOptionsResult(campaignScoped.Take(limit).ToList()));

        var options = proposals.GetProposals(paramDef, prefix, indexService.Current)
            .Take(limit)
            .Select(p => new StoryParamOptionDto(p.Label, p.Detail))
            .ToList();
        return Task.FromResult(new GetStoryParamOptionsResult(options));
    }

    /// <summary>
    ///     Candidates for the campaign-scoped story referenceTypes, from the campaign model:
    ///     event names, branches, thread files. Null when the referenceType is not one of them
    ///     (flags/notifications included — their index symbols already give better coverage,
    ///     e.g. Lua-side Story_Event ids, and the generic provider handles them).
    /// </summary>
    private IEnumerable<StoryParamOptionDto>? CampaignScopedOptions(
        string? referenceType, string campaign, string prefix)
    {
        if (referenceType is not (StoryReferenceTypes.EventName or StoryReferenceTypes.Branch
            or StoryReferenceTypes.PlotFile))
            return null;

        var model = modelService.GetCampaignModel(campaign);
        if (model is null) return [];

        var events = model.Threads.SelectMany(t => t.Events);
        IEnumerable<string> names = referenceType switch
        {
            StoryReferenceTypes.EventName => events.Select(e => e.Name),
            StoryReferenceTypes.Branch => events
                .Select(e => e.Branch)
                .Where(b => !string.IsNullOrEmpty(b))!,
            _ => model.Threads.Select(t => FileNameOf(t.DocumentUri)),
        };

        return names
            .Where(n => prefix.Length == 0 || n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => new StoryParamOptionDto(n));
    }

    private static string FileNameOf(string uri)
    {
        var idx = uri.LastIndexOf('/');
        return idx < 0 ? uri : uri[(idx + 1)..];
    }
}

public sealed class GetStoryDiagnosticsHandler(
    IStoryModelService modelService,
    IGameIndexService indexService,
    IXmlDiagnosticsCollector collector,
    IDocumentTextSource textSource,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetStoryDiagnosticsParams, GetStoryDiagnosticsResult>
{
    public Task<GetStoryDiagnosticsResult> Handle(GetStoryDiagnosticsParams request, CancellationToken ct)
    {
        if (StoryEditorFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new GetStoryDiagnosticsResult([], rejection));

        var model = modelService.GetCampaignModel(request.Campaign);
        if (model is null)
            return Task.FromResult(new GetStoryDiagnosticsResult([],
                $"Campaign '{request.Campaign}' was not found."));

        // Graph node per event INSTANCE (reference identity — duplicate names get distinct
        // disambiguated node ids, and StoryEvent's record equality would conflate them).
        var nodeByEvent = new Dictionary<StoryEvent, string>(ReferenceEqualityComparer.Instance);
        foreach (var node in model.Graph.Nodes)
            if (node.Kind == StoryNodeKind.Event && node.Event is not null)
                nodeByEvent.TryAdd(node.Event, node.Id);

        var index = indexService.Current;
        var results = new List<StoryDiagnosticDto>();
        foreach (var thread in model.Threads)
        {
            ct.ThrowIfCancellationRequested();
            var text = textSource.GetText(thread.DocumentUri)?.Text;
            if (text is null) continue;

            StoryDiagnosticsCorrelator.Correlate(
                thread.DocumentUri, thread.Events,
                collector.Collect(thread.DocumentUri, text, index),
                e => nodeByEvent.GetValueOrDefault(e),
                results);
        }

        return Task.FromResult(new GetStoryDiagnosticsResult(results));
    }
}

public sealed class ResolveStoryReferenceHandler(
    IGameIndexService indexService,
    ISchemaProvider schema,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<ResolveStoryReferenceParams, ResolveStoryReferenceResult>
{
    public Task<ResolveStoryReferenceResult> Handle(ResolveStoryReferenceParams request, CancellationToken ct)
    {
        if (StoryEditorFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new ResolveStoryReferenceResult(Error: rejection));
        if (string.IsNullOrWhiteSpace(request.Value))
            return Task.FromResult(new ResolveStoryReferenceResult(Error: "Nothing to resolve."));

        var index = indexService.Current;
        var symbol = Resolve(index, request.Value.Trim(), request.ReferenceType);
        if (symbol is null)
            return Task.FromResult(new ResolveStoryReferenceResult(
                Error: $"'{request.Value}' does not resolve to any known definition."));

        if (symbol.Origin is not FileOrigin origin)
            return Task.FromResult(new ResolveStoryReferenceResult(
                Error: $"'{request.Value}' is defined in the base game or an archive — " +
                       "there is no workspace file to open."));

        return Task.FromResult(new ResolveStoryReferenceResult(
            origin.Uri, origin.Line, origin.Column ?? 0));
    }

    private GameSymbol? Resolve(GameIndex index, string value, string? referenceType)
    {
        // Prefer a type-matched definition when the referenceType names a concrete symbol type;
        // umbrella names (GameObjectType) and unknown types fall back to the untyped winner.
        var preferred = referenceType switch
        {
            null => null,
            StoryReferenceTypes.EventName => StoryReferenceTypes.EventSymbol,
            StoryReferenceTypes.Notification => StoryReferenceTypes.NotificationSymbol,
            StoryReferenceTypes.Flag => StoryReferenceTypes.FlagSymbol,
            _ when string.Equals(referenceType, "GameObjectType", StringComparison.OrdinalIgnoreCase) => null,
            _ when schema.GetObjectType(referenceType) is not null => referenceType,
            _ => null
        };

        var symbol = preferred is not null ? index.Resolve(value, preferred) : index.Resolve(value);
        if (symbol is not null) return symbol;

        // Scoped ability IDs are indexed as "OWNER$name" while params carry the bare name.
        return index.WorkspaceDefinitions.Keys
            .Where(k => k.IndexOf('$') is var i and >= 0 &&
                        string.Equals(k[(i + 1)..], value, StringComparison.OrdinalIgnoreCase))
            .Select(k => index.Resolve(k))
            .FirstOrDefault(s => s is not null);
    }
}
