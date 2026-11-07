// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.ModBrowser;

namespace PG.StarWarsGame.LSP.Server.Tests.ModBrowser;

/// <summary>
///     The Mod Browser used to be refreshed by the client on <c>$/workspaceScanComplete</c> alone, so
///     a texture dropped into the workspace - re-globbed into the asset catalog by the watcher -
///     stayed out of its tree, table and counts until a sort or a node switch happened to ask again.
///     The catalog apply raises the index-changed event like every other index write; this
///     notifier turns that event into one debounced <c>aet/modBrowserChanged</c>.
/// </summary>
public sealed class ModBrowserChangeNotifierTest
{
    [Fact]
    public void IndexChanged_Fires_SendsModBrowserChanged()
    {
        var sent = new List<string>();
        var indexService = new FakeGameIndexService();
        _ = new ModBrowserChangeNotifier(
            indexService, StartupGates.Open(), m => sent.Add(m),
            NullLogger<ModBrowserChangeNotifier>.Instance, 0);

        indexService.Raise(GameIndex.Empty);

        Assert.Equal(["aet/modBrowserChanged"], sent);
    }

    [Fact]
    public async Task IndexChanged_BurstOfChanges_CollapsesToOneNotification()
    {
        var sent = new List<string>();
        var indexService = new FakeGameIndexService();
        _ = new ModBrowserChangeNotifier(
            indexService, StartupGates.Open(), m => sent.Add(m),
            NullLogger<ModBrowserChangeNotifier>.Instance, 40);

        for (var at = 0; at < 20; at++) indexService.Raise(GameIndex.Empty);

        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.Single(sent);
    }

    /// <summary>A client that has gone away must not take the indexer's thread down with it.</summary>
    [Fact]
    public void SendThrows_IsSwallowed()
    {
        var indexService = new FakeGameIndexService();
        _ = new ModBrowserChangeNotifier(
            indexService, StartupGates.Open(), _ => throw new InvalidOperationException("gone"),
            NullLogger<ModBrowserChangeNotifier>.Instance, 0);

        var exception = Record.Exception(() => indexService.Raise(GameIndex.Empty));

        Assert.Null(exception);
    }
}