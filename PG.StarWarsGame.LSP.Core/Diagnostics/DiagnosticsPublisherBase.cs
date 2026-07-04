// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Shared plumbing for diagnostics publishers: subscribes to index changes, publishes
///     diagnostics for open documents of a given file extension, and clears stale URIs.
///     When debounceMs &gt; 0, rapid consecutive index changes (e.g. from a workspace-wide
///     rename) are batched: only the last change triggers a diagnostic run.
/// </summary>
public abstract class DiagnosticsPublisherBase
{
    private readonly int _debounceMs;
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

    protected DiagnosticsPublisherBase(
        Action<PublishDiagnosticsParams> publish,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        int debounceMs = 100,
        ILogger? logger = null)
    {
        _publish = publish;
        _workspaceHost = workspaceHost;
        _debounceMs = debounceMs;
        _logger = logger ?? NullLogger.Instance;
        indexService.IndexChanged += OnIndexChanged;
    }

    protected abstract string FileExtension { get; }

    protected abstract void PublishForDocument(string uri, string text, GameIndex index);

    protected void Publish(PublishDiagnosticsParams p)
    {
        _publish(p);
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
        var openDocs = _workspaceHost.All
            .Where(d => d.PublishDiagnostics)
            .Where(d => Path.GetExtension(d.Uri).Equals(FileExtension, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var openUris = new HashSet<string>(openDocs.Select(d => d.Uri));

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
                                    || !ReferenceEquals(last.WorkspaceDynamicEnumValues, index.WorkspaceDynamicEnumValues)
                                    || !ReferenceEquals(last.WorkspaceEnumValueDefinitions,
                                        index.WorkspaceEnumValueDefinitions);

        foreach (var doc in openDocs)
        {
            if (!crossDocInputsChanged
                && _lastPublishedUris.Contains(doc.Uri)
                && ReferenceEquals(last!.Documents.GetValueOrDefault(doc.Uri), index.Documents.GetValueOrDefault(doc.Uri)))
                continue;

            try
            {
                PublishForDocument(doc.Uri, doc.Text, index);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception publishing diagnostics for {Uri}; sending empty diagnostics", doc.Uri);
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