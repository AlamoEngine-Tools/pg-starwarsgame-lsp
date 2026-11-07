// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Notifications;

namespace PG.StarWarsGame.LSP.Server.ModBrowser;

/// <summary>
///     Tells the client the Mod Browser's lists may have moved: one debounced
///     <c>aet/modBrowserChanged</c> after a burst of index writes.
/// </summary>
/// <remarks>
///     The browser lists what the index holds - symbols, assets, scripts - and every one of those
///     arrives through an index write that raises the index-changed event: a document edit, a
///     registry reload, the asset catalog re-globbed for a file dropped into the workspace. The
///     client used to refresh the browser on <c>$/workspaceScanComplete</c> alone, so a new texture
///     sat outside its tree, table and counts until a sort or a node switch happened to ask again.
///     Like the preview's notifier it is driven by the index and not by a watcher in the client:
///     the index is the one place that knows a write landed.
/// </remarks>
public sealed class ModBrowserChangeNotifier : DebouncedIndexNotifier
{
    private readonly ILogger<ModBrowserChangeNotifier> _logger;
    private readonly Action<string> _send;

    public ModBrowserChangeNotifier(
        IGameIndexService indexService,
        IStartupGate gate,
        Action<string> send,
        ILogger<ModBrowserChangeNotifier> logger,
        int debounceMs = 300)
        : base(indexService, gate, debounceMs)
    {
        _send = send;
        _logger = logger;
    }

    protected override void Notify()
    {
        try
        {
            _send("aet/modBrowserChanged");
        }
        catch (Exception ex)
        {
            // Runs on the thread that completed the index write; a client that has gone away must
            // not take the indexer down with it.
            _logger.LogWarning(ex, "Failed to send aet/modBrowserChanged (non-fatal)");
        }
    }
}