// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>
///     An index service that only ever hands back the snapshot it was built with. Every mutator is a
///     no-op: handlers under test read <see cref="Current" /> and never write, so recording the
///     writes would only add noise.
/// </summary>
internal sealed class FakeGameIndexService : IGameIndexService
{
    public FakeGameIndexService(GameIndex index)
    {
        Current = index;
    }

    public GameIndex Current { get; }

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
        return new NoopScope();
    }

    public event Action<GameIndex>? IndexChanged
    {
        add { }
        remove { }
    }

    public event Action<ILocalisationIndex>? LocalisationChanged
    {
        add { }
        remove { }
    }

    public event Action<GameIndex>? DynamicEnumChanged
    {
        add { }
        remove { }
    }

    private sealed class NoopScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
