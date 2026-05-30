// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

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
    private readonly Action<PublishDiagnosticsParams> _publish;
    private readonly object _publishLock = new();
    private readonly IGameWorkspaceHost _workspaceHost;
    private HashSet<string> _lastPublishedUris = [];
    private int _pendingVersion;

    protected DiagnosticsPublisherBase(
        Action<PublishDiagnosticsParams> publish,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        int debounceMs = 100)
    {
        _publish = publish;
        _workspaceHost = workspaceHost;
        _debounceMs = debounceMs;
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
            RunPublish(newIndex);
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
            .Where(d => Path.GetExtension(d.Uri).Equals(FileExtension, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var openUris = new HashSet<string>(openDocs.Select(d => d.Uri));

        foreach (var doc in openDocs)
            PublishForDocument(doc.Uri, doc.Text, index);

        foreach (var uri in _lastPublishedUris)
            if (!openUris.Contains(uri))
                _publish(EmptyParams(uri));

        _lastPublishedUris = openUris;
    }
}