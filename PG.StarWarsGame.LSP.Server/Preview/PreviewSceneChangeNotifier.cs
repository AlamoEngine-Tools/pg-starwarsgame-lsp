// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     Pushes <c>aet/previewSceneChanged</c> after the index changes, so an open model preview
///     re-reads the tree instead of showing whatever it was built from when it opened.
///     <para>
///         Driven by <see cref="IGameIndexService.IndexChanged" /> rather than by a file watcher in
///         the client, and that is the point: the event fires once the new index is live, so a
///         preview that re-fetches on it reads the edit. A client watching the file system would be
///         racing the indexer and would sometimes rebuild the scene from the state before the save.
///     </para>
///     <para>
///         Carries no parameters. Which previews an edit affects is not knowable from here - a
///         scene is assembled from a variant chain, its hardpoints, their models and their weapons,
///         and none of that is tracked back to the files it came from. So every open preview is
///         told, and each one asks; the client drops the answer when the scene it gets back is the
///         one it already has, which is what keeps an unrelated edit from costing any geometry.
///     </para>
/// </summary>
public sealed class PreviewSceneChangeNotifier
{
    private readonly int _debounceMs;
    private readonly ILogger<PreviewSceneChangeNotifier> _logger;
    private readonly Action<string> _send;
    private int _pendingVersion;

    public PreviewSceneChangeNotifier(
        IGameIndexService indexService,
        Action<string> send,
        ILogger<PreviewSceneChangeNotifier> logger,
        int debounceMs = 100)
    {
        _send = send;
        _logger = logger;
        _debounceMs = debounceMs;
        indexService.IndexChanged += OnIndexChanged;
    }

    private void OnIndexChanged(GameIndex index)
    {
        if (_debounceMs <= 0)
        {
            Notify();
            return;
        }

        // The same collapse the story notifier does: a workspace-wide rename raises this event
        // hundreds of times, and each one would otherwise cost every open preview a scene rebuild.
        var version = Interlocked.Increment(ref _pendingVersion);
        _ = Task.Run(async () =>
        {
            await Task.Delay(_debounceMs);
            if (Volatile.Read(ref _pendingVersion) != version) return;
            Notify();
        });
    }

    private void Notify()
    {
        try
        {
            _send("aet/previewSceneChanged");
        }
        catch (Exception ex)
        {
            // Runs on the thread that completed the index write. A client that has gone away, or a
            // facade mid-shutdown, must not take the indexer down with it - a preview is a
            // convenience and its refresh is the least important thing in the process.
            _logger.LogWarning(ex, "Failed to send aet/previewSceneChanged (non-fatal).");
        }
    }
}
