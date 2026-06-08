// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PG.StarWarsGame.LSP.Core.Workspace;

public sealed class GameWorkspaceHost : IGameWorkspaceHost
{
    private readonly ConcurrentDictionary<string, TrackedDocument> _docs =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<GameWorkspaceHost> _logger;

    public GameWorkspaceHost(ILogger<GameWorkspaceHost> logger)
    {
        _logger = logger;
    }

    public void AddOrUpdate(string uri, string text, int version)
    {
        var isNew = !_docs.ContainsKey(uri);
        _docs[uri] = new TrackedDocument(uri, text, version);
        _logger.LogDebug("{Action} {Uri} (version={Version}, length={Length})",
            isNew ? "Opened" : "Updated", uri, version, text.Length);
    }

    public void Remove(string uri)
    {
        _docs.TryRemove(uri, out _);
        _logger.LogDebug("Closed {Uri}", uri);
    }

    public bool TryGet(string uri, out TrackedDocument doc)
    {
        return _docs.TryGetValue(uri, out doc!);
    }

    public IEnumerable<TrackedDocument> All => _docs.Values;
}