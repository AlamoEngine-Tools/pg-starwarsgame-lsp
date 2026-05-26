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
/// </summary>
public abstract class DiagnosticsPublisherBase
{
    private readonly Action<PublishDiagnosticsParams> _publish;
    private readonly IGameWorkspaceHost _workspaceHost;
    private HashSet<string> _lastPublishedUris = [];

    protected DiagnosticsPublisherBase(
        Action<PublishDiagnosticsParams> publish,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost)
    {
        _publish = publish;
        _workspaceHost = workspaceHost;
        indexService.IndexChanged += OnIndexChanged;
    }

    protected abstract string FileExtension { get; }

    protected abstract void PublishForDocument(string uri, string text, GameIndex index);

    protected void Publish(PublishDiagnosticsParams p)
    {
        _publish(p);
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
        var openDocs = _workspaceHost.All
            .Where(d => Path.GetExtension(d.Uri).Equals(FileExtension, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var openUris = new HashSet<string>(openDocs.Select(d => d.Uri));

        foreach (var doc in openDocs)
            PublishForDocument(doc.Uri, doc.Text, newIndex);

        foreach (var uri in _lastPublishedUris)
            if (!openUris.Contains(uri))
                _publish(EmptyParams(uri));

        _lastPublishedUris = openUris;
    }
}