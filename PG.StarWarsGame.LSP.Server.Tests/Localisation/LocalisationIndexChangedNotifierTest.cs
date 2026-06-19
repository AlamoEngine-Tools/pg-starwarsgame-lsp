// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class LocalisationIndexChangedNotifierTest
{
    [Fact]
    public void IndexChanged_Fires_SendsLocalisationIndexUpdatedNotification()
    {
        var sent = new List<string>();
        var indexService = new RaisableIndexService();
        _ = new LocalisationIndexChangedNotifier(
            indexService,
            m => sent.Add(m),
            NullLogger<LocalisationIndexChangedNotifier>.Instance);

        indexService.Raise(GameIndex.Empty);

        Assert.Contains("aet/localisationIndexUpdated", sent);
    }

    [Fact]
    public void IndexChanged_FiresMultipleTimes_NotificationSentEachTime()
    {
        var sent = new List<string>();
        var indexService = new RaisableIndexService();
        _ = new LocalisationIndexChangedNotifier(
            indexService,
            m => sent.Add(m),
            NullLogger<LocalisationIndexChangedNotifier>.Instance);

        indexService.Raise(GameIndex.Empty);
        indexService.Raise(GameIndex.Empty);
        indexService.Raise(GameIndex.Empty);

        Assert.Equal(3, sent.Count);
    }

    [Fact]
    public void IndexChanged_SenderThrows_ExceptionDoesNotPropagate()
    {
        var indexService = new RaisableIndexService();
        _ = new LocalisationIndexChangedNotifier(
            indexService,
            _ => throw new InvalidOperationException("boom"),
            NullLogger<LocalisationIndexChangedNotifier>.Instance);

        var ex = Record.Exception(() => indexService.Raise(GameIndex.Empty));

        Assert.Null(ex);
    }

    [Fact]
    public void NoNotifierConstructed_IndexChangedFires_NothingSent()
    {
        var sent = new List<string>();
        var indexService = new RaisableIndexService();

        // No notifier ever constructed — raising the event must not call our spy
        indexService.Raise(GameIndex.Empty);

        Assert.Empty(sent);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class RaisableIndexService : IGameIndexService
    {
        public GameIndex Current => GameIndex.Empty;

        public event Action<GameIndex>? IndexChanged;

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

        public IDisposable BeginBulkUpdate()
        {
            return NullDisposable.Instance;
        }

        public void Raise(GameIndex index)
        {
            IndexChanged?.Invoke(index);
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