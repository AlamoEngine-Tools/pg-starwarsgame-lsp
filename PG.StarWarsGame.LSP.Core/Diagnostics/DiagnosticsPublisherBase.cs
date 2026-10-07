// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Shared plumbing for diagnostics publishers: subscribes to index changes, publishes
///     diagnostics for open documents of a given file extension, and clears stale URIs.
///     When debounceMs &gt; 0, rapid consecutive index changes (e.g. from a workspace-wide
///     rename) are batched: only the last change triggers a diagnostic run.
///     Subclasses can gate publishing entirely via <see cref="DiagnosticsEnabled" /> (used for
///     the per-language diagnostics feature flags): while it returns false, index changes
///     publish nothing.
/// </summary>
public abstract class DiagnosticsPublisherBase : IDiagnosticsRepublisher
{
    private readonly int _debounceMs;
    private readonly IGlobalSuppressionStore? _globalSuppressions;
    private readonly IGameIndexService _indexService;
    private readonly ILogger _logger;
    private readonly Action<PublishDiagnosticsParams> _publish;
    private readonly object _publishLock = new();
    private readonly IGameWorkspaceHost _workspaceHost;
    private HashSet<string> _lastPublishedUris = [];

    // The index the last publish run was based on, for scoping the next run: documents whose
    // entry (and every cross-document input) is reference-identical to the last run cannot have
    // different diagnostics and are skipped. Guarded by _publishLock.
    private GameIndex? _lastRunIndex;
    private int _pendingVersion;

    /// <summary>Every diagnostic this server publishes names it, with the project when one is loaded.</summary>
    private const string SourceName = "aetswg";

    private readonly IProjectLayerMap? _layerMap;

    protected DiagnosticsPublisherBase(
        Action<PublishDiagnosticsParams> publish,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        int debounceMs = 100,
        ILogger? logger = null,
        IGlobalSuppressionStore? globalSuppressions = null,
        IProjectLayerMap? layerMap = null)
    {
        _layerMap = layerMap;
        _publish = publish;
        _workspaceHost = workspaceHost;
        _debounceMs = debounceMs;
        _logger = logger ?? NullLogger.Instance;
        _globalSuppressions = globalSuppressions;
        _indexService = indexService;
        indexService.IndexChanged += OnIndexChanged;
    }

    protected abstract string FileExtension { get; }

    /// <summary>Feature-flag gate: while false, index changes publish nothing.</summary>
    protected virtual bool DiagnosticsEnabled => true;

    /// <summary>
    ///     Republishes every open document of this language against the current index.
    ///     <para>
    ///         The per-document skip that <see cref="RunPublish" /> applies is deliberately bypassed:
    ///         it compares index references to decide what can have changed, and the reason for
    ///         republishing here is something the index does not model at all.
    ///     </para>
    /// </summary>
    public virtual Task RepublishAllAsync(CancellationToken ct)
    {
        if (!DiagnosticsEnabled) return Task.CompletedTask;

        lock (_publishLock)
        {
            _lastRunIndex = null;
            RunPublish(_indexService.Current);
        }

        return Task.CompletedTask;
    }

    protected abstract void PublishForDocument(string uri, string text, GameIndex index);

    /// <summary>
    ///     Republishes one document against the current index, when it is open. For an input the
    ///     index does not carry - a finding arriving from outside, such as the Lua analyzer's -
    ///     that changes one document's diagnostics and no other.
    /// </summary>
    protected void RepublishDocument(string uri)
    {
        if (!DiagnosticsEnabled || !_workspaceHost.TryGet(uri, out var doc)) return;

        lock (_publishLock)
        {
            try
            {
                PublishForDocument(doc.Uri, doc.Text, _indexService.Current);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception republishing diagnostics for {Uri}", doc.Uri);
            }
        }
    }

    /// <summary>
    ///     Drops every diagnostic silenced by <paramref name="ranges" /> or by a project-wide
    ///     suppression. This is where "suppressed" is defined for all languages - the merge with the
    ///     global store and the fail-safe on unnameable diagnostics live here, so a language decides
    ///     only <em>where</em> in its pipeline to apply it, never <em>what it means</em>.
    ///     <para>
    ///         Callers apply it where they hold both the diagnostics and the parsed document the
    ///         ranges come from, which every publisher already does when it builds them. Filtering
    ///         at publish time instead would re-resolve the document for a second time, and would
    ///         read the saved text rather than the staged text an on-demand caller passed in.
    ///     </para>
    /// </summary>
    protected IReadOnlyList<Diagnostic> FilterSuppressed(
        IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<SuppressionRange> ranges)
    {
        if (diagnostics.Count == 0) return diagnostics;

        var global = _globalSuppressions?.GetAll() ?? [];
        if (ranges.Count == 0 && global.Count == 0) return diagnostics;

        var suppressions = new DocumentSuppressions(ranges, global);
        return diagnostics.Where(d => !IsSuppressed(d, suppressions)).ToList();
    }

    protected void Publish(PublishDiagnosticsParams p)
    {
        _publish(WithOrigin(p));
    }

    /// <summary>
    ///     Stamps where a diagnostic came from. One server runs per open project, so a Problems
    ///     view is fed by several servers at once: the <c>source</c> names the project that raised
    ///     it, and a diagnostic on a dependency's document names that layer in its related
    ///     information. Applied here, once, rather than at every site that builds a
    ///     <see cref="Diagnostic" />; a source a producer set itself is kept.
    /// </summary>
    private PublishDiagnosticsParams WithOrigin(PublishDiagnosticsParams p)
    {
        if (!p.Diagnostics.Any())
            return p;

        var source = SourceName;
        string? layerName = null;
        var layers = _layerMap?.Layers;
        if (_layerMap is not null && layers is { Count: > 0 })
        {
            var topRank = layers.Max(l => l.Rank);
            var project = _layerMap.GetLayerName(topRank);
            if (!string.IsNullOrEmpty(project))
                source = $"{SourceName} ({project})";
            else
                _logger.LogDebug("Diagnostic origin: the top layer (rank {Rank}) has no name; {Count} layer(s) known",
                    topRank, layers.Count);

            // Unencoded: the layer map's prefixes come from PathToFileUri ("file:///c:/..."), and
            // the encoded form ("c%3A") would match nothing and read as the top layer.
            var rank = _layerMap.GetRank(p.Uri.ToUnencodedString());
            if (rank < topRank)
                layerName = _layerMap.GetLayerName(rank);
        }

        if (_layerMap is null)
            _logger.LogDebug(
                "Diagnostic origin: no layer map wired into {Publisher} (suppressions wired: {Suppressions})",
                GetType().Name, _globalSuppressions is not null);

        var stamped = p.Diagnostics.Select(d => Stamp(d, source, layerName, p.Uri)).ToList();
        return new PublishDiagnosticsParams
        {
            Uri = p.Uri,
            Version = p.Version,
            Diagnostics = new Container<Diagnostic>(stamped)
        };
    }

    private static Diagnostic Stamp(Diagnostic d, string source, string? layerName, DocumentUri uri)
    {
        var related = d.RelatedInformation;
        if (layerName is not null)
        {
            var mine = new DiagnosticRelatedInformation
            {
                Location = new Location { Uri = uri, Range = d.Range },
                Message = "Layer - " + layerName
            };
            related = related is null
                ? new Container<DiagnosticRelatedInformation>(mine)
                : new Container<DiagnosticRelatedInformation>(related.Append(mine));
        }

        return new Diagnostic
        {
            Range = d.Range,
            Severity = d.Severity,
            Code = d.Code,
            CodeDescription = d.CodeDescription,
            // The producers stamp the server's id; that is what the project tag replaces. A source
            // that is not ours (a producer forwarding another tool's finding) stays as it is.
            Source = d.Source is null || d.Source == AppProperties.LspServerId ? source : d.Source,
            Message = d.Message,
            Tags = d.Tags,
            RelatedInformation = related,
            Data = d.Data
        };
    }

    /// <summary>
    ///     A diagnostic with no parseable id cannot be named by a suppression, so it always
    ///     survives. That is the safe direction: an unsuppressable diagnostic stays visible, and is
    ///     never silently dropped for lacking a code.
    /// </summary>
    private static bool IsSuppressed(Diagnostic diagnostic, DocumentSuppressions suppressions)
    {
        return diagnostic.Code?.String is { } code
               && DiagnosticId.TryParse(code, out var id)
               && suppressions.IsSuppressed(id, diagnostic.Range.Start.Line);
    }

    protected void ClearAllPublished()
    {
        foreach (var uri in _lastPublishedUris)
            _publish(EmptyParams(uri));
        _lastPublishedUris = [];
    }

    protected static PublishDiagnosticsParams EmptyParams(string uri)
    {
        return new PublishDiagnosticsParams
        {
            Uri = DocumentUri.From(uri),
            Diagnostics = new Container<Diagnostic>()
        };
    }

    private void OnIndexChanged(GameIndex newIndex)
    {
        if (_debounceMs <= 0)
        {
            lock (_publishLock)
            {
                RunPublish(newIndex);
            }

            return;
        }

        // Assign a version to this change. Only the highest version (the latest
        // IndexChanged after the debounce window) actually runs diagnostics.
        var version = Interlocked.Increment(ref _pendingVersion);
        _ = Task.Run(async () =>
        {
            await Task.Delay(_debounceMs);
            if (Volatile.Read(ref _pendingVersion) != version) return;
            lock (_publishLock)
            {
                if (Volatile.Read(ref _pendingVersion) != version) return;
                RunPublish(newIndex);
            }
        });
    }

    private void RunPublish(GameIndex index)
    {
        if (!DiagnosticsEnabled) return;

        var openDocs = _workspaceHost.All
            .Where(d => d.PublishDiagnostics)
            .Where(d => Path.GetExtension(d.Uri).Equals(FileExtension, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var openUris = new HashSet<string>(openDocs.Select(d => d.Uri), DocumentUris.Comparer);

        // Reference-diff against the last run: a document's diagnostics can only change when its
        // own Documents entry changed or when any cross-document input (symbols, references,
        // groups, baseline, localisation, assets, bones, enums) changed. GameIndex is an immutable
        // record, so unchanged fields keep their references across updates; a content-only edit
        // replaces just the edited document's entry.
        var last = _lastRunIndex;
        var crossDocInputsChanged = last is null
                                    || !ReferenceEquals(last.WorkspaceDefinitions, index.WorkspaceDefinitions)
                                    || !ReferenceEquals(last.WorkspaceReferences, index.WorkspaceReferences)
                                    || !ReferenceEquals(last.WorkspaceGroupMemberships, index.WorkspaceGroupMemberships)
                                    || !ReferenceEquals(last.Baseline, index.Baseline)
                                    || !ReferenceEquals(last.Localisation, index.Localisation)
                                    || !ReferenceEquals(last.AssetFiles, index.AssetFiles)
                                    || !ReferenceEquals(last.ModelBones, index.ModelBones)
                                    || !ReferenceEquals(last.WorkspaceDynamicEnumValues,
                                        index.WorkspaceDynamicEnumValues)
                                    || !ReferenceEquals(last.WorkspaceEnumValueDefinitions,
                                        index.WorkspaceEnumValueDefinitions);

        foreach (var doc in openDocs)
        {
            if (!crossDocInputsChanged
                && _lastPublishedUris.Contains(doc.Uri)
                && ReferenceEquals(last!.Documents.GetValueOrDefault(doc.Uri),
                    index.Documents.GetValueOrDefault(doc.Uri)))
                continue;

            try
            {
                PublishForDocument(doc.Uri, doc.Text, index);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception publishing diagnostics for {Uri}; sending empty diagnostics",
                    doc.Uri);
                _publish(EmptyParams(doc.Uri));
            }
        }

        foreach (var uri in _lastPublishedUris)
            if (!openUris.Contains(uri))
                _publish(EmptyParams(uri));

        _lastPublishedUris = openUris;
        _lastRunIndex = index;
    }
}