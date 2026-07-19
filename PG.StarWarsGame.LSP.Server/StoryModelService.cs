// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server;

public interface IStoryModelService
{
    IReadOnlyList<string> GetCampaignNames();
    StoryCampaignModel? GetCampaignModel(string campaignName);

    /// <summary>Every campaign model whose thread closure contains the given document.</summary>
    IReadOnlyList<StoryCampaignModel> GetModelsContaining(string canonicalUri);

    /// <summary>The current chain scan (campaign → faction → manifest → thread associations).</summary>
    StoryChainScanResult GetChainResult();

    /// <summary>
    ///     Campaign names whose cached model no longer matches the current index (or every
    ///     campaign when the chain itself went stale) - without rebuilding anything. Feeds the
    ///     <c>aet/storyGraphChanged</c> notification.
    /// </summary>
    IReadOnlyList<string> GetInvalidatedCampaigns();
}

/// <summary>
///     Lazily builds and caches per-campaign story models. Validation is on-access: a cached
///     chain or model is reused only while every contributing document's <see cref="GameIndex" />
///     version is unchanged - an edit to a campaign, manifest, or thread invalidates exactly the
///     models it feeds. Content is read open-buffer-first (via <see cref="IDocumentTextSource" />)
///     so unsaved edits shape the model; layer precedence comes from searching the xml roots
///     highest-rank-first, mirroring the discovery scan.
/// </summary>
public sealed class StoryModelService : IStoryModelService
{
    private readonly IFileHelper _fileHelper;

    private readonly object _gate = new();
    private readonly IGameIndexService _indexService;
    private readonly ILogger<StoryModelService> _logger;
    private readonly Dictionary<string, ModelCache> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly IModProjectReloadService _reloadService;
    private readonly ISchemaProvider _schema;
    private readonly IDocumentTextSource _textSource;
    private ChainCache? _chain;

    // True after a request was answered from a scan that read no documents (startup window:
    // workspace config/schema not yet published). Such an answer was served to a client, so
    // index changes must re-trigger a scan even though no chain is cached.
    private bool _servedIncompleteScan;

    public StoryModelService(
        IModProjectReloadService reloadService,
        IGameIndexService indexService,
        ISchemaProvider schema,
        IFileHelper fileHelper,
        IDocumentTextSource textSource,
        ILogger<StoryModelService> logger)
    {
        _reloadService = reloadService;
        _indexService = indexService;
        _schema = schema;
        _fileHelper = fileHelper;
        _textSource = textSource;
        _logger = logger;
    }

    public IReadOnlyList<string> GetCampaignNames()
    {
        return GetChain().Result.Campaigns
            .Select(c => c.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public StoryCampaignModel? GetCampaignModel(string campaignName)
    {
        var chain = GetChain();

        lock (_gate)
        {
            if (_models.TryGetValue(campaignName, out var cached)
                && ReferenceEquals(cached.Chain, chain)
                && VersionsMatch(cached.DocumentVersions))
                return cached.Model;
        }

        var reader = new RecordingReader(this);
        var model = new StoryCampaignAssembler(_schema)
            .Assemble(campaignName, chain.Result, reader.ReadThread);
        if (model is null) return null;

        _logger.LogDebug("Story model for campaign '{Campaign}' built: {Threads} thread(s)",
            campaignName, model.Threads.Count);

        lock (_gate)
        {
            _models[campaignName] = new ModelCache(model, chain, reader.Versions);
        }

        return model;
    }

    public IReadOnlyList<StoryCampaignModel> GetModelsContaining(string canonicalUri)
    {
        var result = new List<StoryCampaignModel>();
        foreach (var name in GetCampaignNames())
            if (GetCampaignModel(name) is { } model
                && model.Threads.Any(t => string.Equals(t.DocumentUri, canonicalUri, StringComparison.Ordinal)))
                result.Add(model);
        return result;
    }

    public StoryChainScanResult GetChainResult()
    {
        return GetChain().Result;
    }

    public IReadOnlyList<string> GetInvalidatedCampaigns()
    {
        List<string> invalidated;
        lock (_gate)
        {
            if (_chain is null && !_servedIncompleteScan) return [];

            if (_chain is not null && VersionsMatch(_chain.DocumentVersions))
                return _models
                    .Where(kvp => !VersionsMatch(kvp.Value.DocumentVersions))
                    .Select(kvp => kvp.Value.Model.CampaignName)
                    .ToList();

            invalidated = _chain?.Result.Campaigns.Select(c => c.Name).ToList() ?? [];
        }

        // The chain is stale - or a client was answered from an incomplete startup-window scan.
        // Rescanning here is cheap (a handful of registry/manifest reads) and lets the change
        // notification name campaigns that only became resolvable after startup finished. The
        // old names stay included so clients holding a since-renamed campaign refresh too.
        invalidated.AddRange(GetChain().Result.Campaigns.Select(c => c.Name));
        return invalidated.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ── Chain scan (campaign → manifest → thread associations) ───────────────

    private ChainCache GetChain()
    {
        lock (_gate)
        {
            if (_chain is not null && VersionsMatch(_chain.DocumentVersions))
                return _chain;
        }

        var resolver = new RecordingResolver(this);
        var result = StoryChainScanResult.Empty;
        foreach (var def in _schema.AllMetafiles.Where(d => d.MetafileType == MetafileType.Special))
        {
            // The highest layer's registry wins (the engine replaces rather than merges).
            var scan = new StoryChainScanner(resolver).Scan(def.Path);
            if (!ReferenceEquals(scan, StoryChainScanResult.Empty))
                result = scan;
        }

        var chain = new ChainCache(result, resolver.Versions);

        // A scan that read nothing must not be cached: an empty version map matches every
        // future index state, which would pin the empty result forever. This happens inside
        // the startup window (workspace config / schema not yet published) and in workspaces
        // without story data - in both cases the next access simply rescans (cheap: nothing
        // was readable).
        if (resolver.Versions.Count == 0)
        {
            lock (_gate)
            {
                _servedIncompleteScan = true;
            }

            return chain;
        }

        lock (_gate)
        {
            _chain = chain;
            _models.Clear();
            _servedIncompleteScan = false;
        }

        return chain;
    }

    private IReadOnlyList<string> XmlRootsHighestFirst()
    {
        var roots = _reloadService.LastWorkspaceConfig?.XmlDirectories ?? [];
        return roots.Reverse().ToList();
    }

    private (string Uri, string Text)? ReadXmlRelative(string xmlRelativePath)
    {
        foreach (var root in XmlRootsHighestFirst())
        {
            var path = _fileHelper.FindInWorkspace([root], xmlRelativePath);
            if (path is null) continue;
            var uri = _fileHelper.NormalizeUri(path);
            if (_textSource.GetText(uri) is { } text)
                return (uri, text.Text);
        }

        return null;
    }

    private bool VersionsMatch(IReadOnlyDictionary<string, int?> recorded)
    {
        var documents = _indexService.Current.Documents;
        foreach (var (uri, version) in recorded)
            if ((documents.TryGetValue(uri, out var doc) ? doc.Version : null) != version)
                return false;
        return true;
    }

    private int? CurrentVersionOf(string uri)
    {
        return _indexService.Current.Documents.TryGetValue(uri, out var doc) ? doc.Version : null;
    }

    private sealed record ChainCache(StoryChainScanResult Result, IReadOnlyDictionary<string, int?> DocumentVersions);

    private sealed record ModelCache(
        StoryCampaignModel Model,
        ChainCache Chain,
        IReadOnlyDictionary<string, int?> DocumentVersions);

    /// <summary>Chain-scan resolver over the workspace, recording every read for invalidation.</summary>
    private sealed class RecordingResolver(StoryModelService service) : IStoryChainFileResolver
    {
        public Dictionary<string, int?> Versions { get; } = new(StringComparer.Ordinal);

        public StoryChainFile? ReadFile(string xmlRelativePath)
        {
            var read = service.ReadXmlRelative(xmlRelativePath);
            if (read is null) return null;
            Versions[read.Value.Uri] = service.CurrentVersionOf(read.Value.Uri);
            return new StoryChainFile(read.Value.Text, read.Value.Uri);
        }

        public bool IsKnownToBaseline(string xmlRelativePath)
        {
            var normalized = xmlRelativePath.Replace('\\', '/').ToLowerInvariant();
            var map = service._indexService.Current.Baseline.FileTypeMap;
            return map.ContainsKey(normalized) || map.ContainsKey("data/xml/" + normalized);
        }
    }

    /// <summary>
    ///     Thread reader for the assembler, recording every read for invalidation. Chain-level
    ///     inputs (campaigns, manifests) are covered separately: the model cache pins the
    ///     <see cref="ChainCache" /> instance it was built from, and a chain rebuild clears it.
    /// </summary>
    private sealed class RecordingReader(StoryModelService service)
    {
        public Dictionary<string, int?> Versions { get; } = new(StringComparer.Ordinal);

        public (string Uri, string Text)? ReadThread(string xmlRelativePath)
        {
            var read = service.ReadXmlRelative(xmlRelativePath);
            if (read is null) return null;
            Versions[read.Value.Uri] = service.CurrentVersionOf(read.Value.Uri);
            return read;
        }
    }
}