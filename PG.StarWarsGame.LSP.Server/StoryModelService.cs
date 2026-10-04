// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     One campaign faction, which is the unit a story model is built and cached under.
///     <para>
///         Compared without regard to case, because the campaign name comes from an XML attribute
///         and the faction from a tag name, and neither is written consistently across the corpus.
///     </para>
/// </summary>
public sealed record StoryModelKey(string Campaign, string Faction)
{
    public bool Equals(StoryModelKey? other)
    {
        return other is not null
               && string.Equals(Campaign, other.Campaign, StringComparison.OrdinalIgnoreCase)
               && string.Equals(Faction, other.Faction, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Campaign),
            StringComparer.OrdinalIgnoreCase.GetHashCode(Faction));
    }

    public override string ToString()
    {
        return $"{Campaign} - {Faction}";
    }
}

public interface IStoryModelService
{
    IReadOnlyList<string> GetCampaignNames();

    /// <summary>Every campaign faction pair the chain declares, in document order.</summary>
    IReadOnlyList<StoryModelKey> GetModelKeys();

    /// <summary>
    ///     One campaign FACTION's model, or null when the campaign declares no manifest for it.
    ///     A campaign's factions are separate story chains and are never merged - see
    ///     <see cref="StoryCampaignModel" />.
    /// </summary>
    StoryCampaignModel? GetCampaignModel(string campaignName, string faction);

    /// <summary>Every model whose thread closure contains the given document.</summary>
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
public sealed class StoryModelService : IStoryModelService, ICacheStatisticsSource
{
    private readonly IFileHelper _fileHelper;

    private readonly object _gate = new();
    private readonly IGameIndexService _indexService;
    private readonly ILogger<StoryModelService> _logger;
    private readonly Dictionary<StoryModelKey, ModelCache> _models = new();
    private readonly IModProjectReloadService _reloadService;
    private readonly ISchemaProvider _schema;
    private readonly IDocumentTextSource _textSource;
    private ChainCache? _chain;

    // True after a request was answered from a scan that read no documents (startup window:
    // workspace config/schema not yet published). Such an answer was served to a client, so
    // index changes must re-trigger a scan even though no chain is cached.
    private bool _servedIncompleteScan;

    /// <summary>Assembled campaign models held, one per (campaign, faction) asked for.</summary>
    public CacheStatistics Snapshot()
    {
        lock (_gate)
        {
            return new CacheStatistics("story-models", _models.Count);
        }
    }

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

    public IReadOnlyList<StoryModelKey> GetModelKeys()
    {
        var keys = new List<StoryModelKey>();
        var seen = new HashSet<StoryModelKey>();
        foreach (var campaign in GetChain().Result.Campaigns)
        foreach (var manifest in campaign.FactionManifests)
        {
            var key = new StoryModelKey(campaign.Name, manifest.Faction);
            if (seen.Add(key)) keys.Add(key);
        }

        return keys;
    }

    public StoryCampaignModel? GetCampaignModel(string campaignName, string faction)
    {
        var chain = GetChain();
        var key = new StoryModelKey(campaignName, faction);

        lock (_gate)
        {
            if (_models.TryGetValue(key, out var cached)
                && ReferenceEquals(cached.Chain, chain)
                && VersionsMatch(cached.DocumentVersions))
                return cached.Model;
        }

        var reader = new RecordingReader(this);
        var model = new StoryCampaignAssembler(_schema)
            .Assemble(campaignName, faction, chain.Result, reader.ReadThread, reader.ReadLuaMachine);
        if (model is null) return null;

        _logger.LogDebug("Story model for {Key} built: {Threads} thread(s)",
            key, model.Threads.Count);

        lock (_gate)
        {
            _models[key] = new ModelCache(model, chain, reader.Versions);
        }

        return model;
    }

    /// <summary>
    ///     The campaign models whose threads include <paramref name="canonicalUri" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The CHAIN narrows the candidates; the MODEL still decides. That order is the whole
    ///         point: asking each model directly meant assembling every campaign model in the
    ///         workspace to answer a question about one document, and models are invalidated by an
    ///         edit to anything they read - so this ran on every keystroke in a story file, not
    ///         once. MEASURED on a real layered mod: opening one story file took 7.9s and the next
    ///         6.7s, with 7,553 model assemblies logged.
    ///     </para>
    ///     <para>
    ///         The chain already records which manifest lists which thread, and a thread is
    ///         recorded as an xml-relative path that the document's URI ends with - so the
    ///         narrowing needs no file reads and no new dependency. The final
    ///         <c>model.Threads</c> test is kept so the ANSWER is exactly what it was: the chain
    ///         can only over-offer candidates, never hide one.
    ///     </para>
    /// </remarks>
    public IReadOnlyList<StoryCampaignModel> GetModelsContaining(string canonicalUri)
    {
        var chain = GetChain().Result;

        var manifestsWithDocument = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in chain.Manifests)
        {
            if (!manifest.ActiveThreads.Any(t => IsThreadOf(canonicalUri, t))
                && !manifest.SuspendedThreads.Any(t => IsThreadOf(canonicalUri, t)))
                continue;

            manifestsWithDocument.Add(manifest.ManifestFile);
        }

        // Not a thread of any manifest - no model can contain it, and none is built to find out.
        if (manifestsWithDocument.Count == 0) return [];

        var result = new List<StoryCampaignModel>();
        var seen = new HashSet<StoryModelKey>();

        foreach (var campaign in chain.Campaigns)
        foreach (var factionManifest in campaign.FactionManifests)
        {
            if (!manifestsWithDocument.Contains(factionManifest.ManifestFile)) continue;
            // Deduplicated like GetModelKeys: a campaign may name the same faction twice.
            if (!seen.Add(new StoryModelKey(campaign.Name, factionManifest.Faction))) continue;

            if (GetCampaignModel(campaign.Name, factionManifest.Faction) is { } model
                && model.Threads.Any(t => string.Equals(t.DocumentUri, canonicalUri, StringComparison.Ordinal)))
                result.Add(model);
        }

        return result;
    }

    /// <summary>
    ///     Whether <paramref name="documentUri" /> is the thread the manifest spells as
    ///     <paramref name="xmlRelativeThread" />.
    /// </summary>
    /// <remarks>
    ///     Anchored at a segment boundary, so <c>Story_Sith.xml</c> is never satisfied by
    ///     <c>Alt_Story_Sith.xml</c>. Manifests spell separators either way, and case never agrees
    ///     between shipped and mod data, so both are folded.
    /// </remarks>
    private static bool IsThreadOf(string documentUri, string xmlRelativeThread)
    {
        if (string.IsNullOrEmpty(xmlRelativeThread)) return false;

        var relative = xmlRelativeThread.Replace('\\', '/').TrimStart('/');
        return documentUri.EndsWith("/" + relative, StringComparison.OrdinalIgnoreCase);
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
            // A mod and its dependencies may each ship the campaign registry, but the engine
            // resolves the name to one file: the highest layer's copy shadows the rest rather than
            // extending them. Scan(string) reads through the resolver, which searches xml roots
            // highest-rank-first - the same rule WorkspaceIndexer.ScanStoryChain applies, so the
            // set of campaigns typed there and modelled here stays identical.
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

        /// <summary>
        ///     The attached script (manifest name, extensionless) as a state machine: the indexed
        ///     .lua document of that file name, read open-buffer-first and extracted statically.
        ///     Recorded like a thread, so an edit to the script rebuilds the model.
        /// </summary>
        public LuaStoryMachine? ReadLuaMachine(string scriptName)
        {
            var suffix = "/" + scriptName.ToLowerInvariant() + ".lua";
            var uri = service._indexService.Current.Documents.Keys
                .FirstOrDefault(u => u.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (uri is null) return null;
            var text = service._textSource.GetText(uri);
            if (text is null) return null;
            Versions[uri] = service.CurrentVersionOf(uri);
            return LuaStoryMachineExtractor.Extract(text.Text, uri);
        }
    }
}