// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

public sealed class PreviewSceneChangeNotifierTest
{
    [Fact]
    public void IndexChanged_Fires_SendsPreviewSceneChanged()
    {
        var sent = new List<string>();
        var indexService = new RaisableIndexService();
        _ = new PreviewSceneChangeNotifier(
            indexService, m => sent.Add(m),
            NullLogger<PreviewSceneChangeNotifier>.Instance, 0);

        indexService.Raise(GameIndex.Empty);

        Assert.Equal(["aet/previewSceneChanged"], sent);
    }

    /// <summary>
    ///     The whole reason this is driven by the index rather than by a file watcher in the client:
    ///     the event fires once the new index is live, so a preview that re-fetches on it reads the
    ///     edit rather than racing it.
    /// </summary>
    [Fact]
    public void IndexChanged_FiresRepeatedly_SendsEachTimeWhenNotDebounced()
    {
        var sent = new List<string>();
        var indexService = new RaisableIndexService();
        _ = new PreviewSceneChangeNotifier(
            indexService, m => sent.Add(m),
            NullLogger<PreviewSceneChangeNotifier>.Instance, 0);

        indexService.Raise(GameIndex.Empty);
        indexService.Raise(GameIndex.Empty);

        Assert.Equal(2, sent.Count);
    }

    /// <summary>
    ///     A workspace-wide rename or a bulk index raises this event a great many times, and every
    ///     one of them would otherwise cost every open preview a scene rebuild.
    /// </summary>
    [Fact]
    public async Task IndexChanged_BurstOfChanges_CollapsesToOneNotification()
    {
        var sent = new List<string>();
        var indexService = new RaisableIndexService();
        _ = new PreviewSceneChangeNotifier(
            indexService, m => sent.Add(m),
            NullLogger<PreviewSceneChangeNotifier>.Instance, 40);

        for (var at = 0; at < 20; at++)
        {
            indexService.Raise(GameIndex.Empty);
        }

        await Task.Delay(300);

        Assert.Single(sent);
    }

    [Fact]
    public async Task IndexChanged_SeparatedByMoreThanTheWindow_SendsBoth()
    {
        var sent = new List<string>();
        var indexService = new RaisableIndexService();
        _ = new PreviewSceneChangeNotifier(
            indexService, m => sent.Add(m),
            NullLogger<PreviewSceneChangeNotifier>.Instance, 40);

        indexService.Raise(GameIndex.Empty);
        await Task.Delay(250);
        indexService.Raise(GameIndex.Empty);
        await Task.Delay(250);

        Assert.Equal(2, sent.Count);
    }

    /// <summary>
    ///     A preview is a convenience. A client that has gone away, or a facade that is mid-shutdown,
    ///     must not take the indexer's thread down with it - this runs on the thread that completed
    ///     the index write.
    /// </summary>
    [Fact]
    public void Send_Throws_ExceptionDoesNotPropagate()
    {
        var indexService = new RaisableIndexService();
        _ = new PreviewSceneChangeNotifier(
            indexService, _ => throw new InvalidOperationException("boom"),
            NullLogger<PreviewSceneChangeNotifier>.Instance, 0);

        var ex = Record.Exception(() => indexService.Raise(GameIndex.Empty));

        Assert.Null(ex);
    }

    /// <summary>
    ///     Localisation applies have their own notification and reach the preview through the index
    ///     like anything else; reacting to both would send two for one change.
    /// </summary>
    [Fact]
    public void LocalisationChanged_Fires_NothingSent()
    {
        var sent = new List<string>();
        var indexService = new RaisableIndexService();
        _ = new PreviewSceneChangeNotifier(
            indexService, m => sent.Add(m),
            NullLogger<PreviewSceneChangeNotifier>.Instance, 0);

        indexService.RaiseLocalisation(GameIndex.Empty.Localisation);

        Assert.Empty(sent);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class RaisableIndexService : IGameIndexService
    {
        public GameIndex Current => GameIndex.Empty;

        public event Action<GameIndex>? IndexChanged;
        public event Action<ILocalisationIndex>? LocalisationChanged;

        public event Action<GameIndex>? DynamicEnumChanged
        {
            add { }
            remove { }
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }

        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return NullDisposable.Instance;
        }

        public void Raise(GameIndex index)
        {
            IndexChanged?.Invoke(index);
        }

        public void RaiseLocalisation(ILocalisationIndex index)
        {
            LocalisationChanged?.Invoke(index);
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
