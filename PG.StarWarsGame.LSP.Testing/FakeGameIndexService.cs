// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>
///     The index service a test hands its handlers (#304): a snapshot the test chooses, replaced by
///     every apply the way the real service replaces it, each one raising <c>IndexChanged</c>; and a
///     record of what the code under test asked of it.
/// </summary>
/// <remarks>
///     <para>
///         A document update is recorded, not parsed: the fake has no parser, and a test that wants a
///         symbol in the snapshot puts it there through <see cref="InjectDocument" /> or
///         <see cref="Current" />. The recording is locked, because the workspace indexer updates
///         from several threads at once.
///     </para>
///     <para>
///         The two-snapshot form stands in for a re-index that lands mid-request: the first read gets
///         one snapshot and every later read another, and <see cref="CurrentReads" /> says how often
///         the code under test went back to the service instead of keeping the snapshot it captured.
///     </para>
/// </remarks>
public sealed class FakeGameIndexService : IGameIndexService
{
    private readonly Lock _gate = new();
    private GameIndex? _afterFirstRead;
    private int _bulkDepth;
    private bool _bulkPending;
    private GameIndex _current;

    public FakeGameIndexService() : this(GameIndex.Empty)
    {
    }

    public FakeGameIndexService(GameIndex current)
    {
        _current = current;
    }

    public FakeGameIndexService(GameIndex first, GameIndex afterFirstRead)
    {
        _current = first;
        _afterFirstRead = afterFirstRead;
    }

    /// <summary>How often <see cref="Current" /> was read.</summary>
    public int CurrentReads { get; private set; }

    /// <summary>Every document update, in order; an open is in <see cref="Opens" /> instead.</summary>
    public List<(string Uri, string Text, int Version)> Updates { get; } = [];

    /// <summary>Every document open, in order.</summary>
    public List<(string Uri, string Text, int Version)> Opens { get; } = [];

    /// <summary>Every document removal, in order.</summary>
    public List<string> Removals { get; } = [];

    /// <summary>Every injected document, in order.</summary>
    public List<DocumentIndex> Injected { get; } = [];

    /// <summary>Every dynamic-enum value set applied, in order.</summary>
    public List<ImmutableDictionary<string, ImmutableArray<string>>> DynamicEnumApplications { get; } = [];

    /// <summary>How often an asset file index was applied.</summary>
    public int AssetApplications { get; private set; }

    /// <summary>How often a bulk update was begun.</summary>
    public int BulkUpdates { get; private set; }

    /// <summary>The snapshot. Setting it replaces the snapshot silently; the applies raise events.</summary>
    public GameIndex Current
    {
        get
        {
            lock (_gate)
            {
                var value = _current;
                CurrentReads++;

                if (_afterFirstRead is not null)
                {
                    _current = _afterFirstRead;
                    _afterFirstRead = null;
                }

                return value;
            }
        }
        set
        {
            lock (_gate)
            {
                _current = value;
            }
        }
    }

    public event Action<GameIndex>? IndexChanged;
    public event Action<ILocalisationIndex>? LocalisationChanged;
    public event Action<GameIndex>? DynamicEnumChanged;

    public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Updates.Add((uri, text, version));
            Stamp(uri, version);
        }

        return Task.CompletedTask;
    }

    public Task OpenDocumentAsync(string uri, string text, int version, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Opens.Add((uri, text, version));
            Stamp(uri, version);
        }

        return Task.CompletedTask;
    }

    public void InjectDocument(DocumentIndex document)
    {
        lock (_gate)
        {
            Injected.Add(document);
            Replace(_current with { Documents = _current.Documents.SetItem(document.DocumentUri, document) });
        }
    }

    public void RemoveDocument(string uri)
    {
        lock (_gate)
        {
            Removals.Add(uri);
            Replace(_current with { Documents = _current.Documents.Remove(uri) });
        }
    }

    public void ApplyBaseline(BaselineIndex baseline)
    {
        lock (_gate)
        {
            Replace(_current with { Baseline = baseline });
            DynamicEnumChanged?.Invoke(_current);
        }
    }

    public void ApplyLocalisation(ILocalisationIndex index)
    {
        lock (_gate)
        {
            Replace(_current with { Localisation = index });
            LocalisationChanged?.Invoke(index);
        }
    }

    public void ApplyAssetFiles(IAssetFileIndex index)
    {
        lock (_gate)
        {
            AssetApplications++;
            Replace(_current with { AssetFiles = index });
        }
    }

    public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
    {
        lock (_gate)
        {
            Replace(_current with { ModelBones = bones });
        }
    }

    public void ApplyModelTextures(ImmutableDictionary<string, ImmutableArray<string>> textures)
    {
        lock (_gate)
        {
            Replace(_current with { ModelTextures = textures });
        }
    }

    public void ApplyParticleSystems(ImmutableHashSet<string> particleSystems)
    {
        lock (_gate)
        {
            Replace(_current with { ParticleSystems = particleSystems });
        }
    }

    public void ApplyModelProxies(ImmutableDictionary<string, ImmutableArray<string>> proxies)
    {
        lock (_gate)
        {
            Replace(_current with { ModelProxies = proxies });
        }
    }

    public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
    {
        lock (_gate)
        {
            DynamicEnumApplications.Add(values);
            Replace(_current with { WorkspaceDynamicEnumValues = values });
            DynamicEnumChanged?.Invoke(_current);
        }
    }

    public void ApplyWorkspaceEnumValueDefinitions(
        ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
    {
        lock (_gate)
        {
            Replace(_current with { WorkspaceEnumValueDefinitions = definitions });
        }
    }

    public IDisposable BeginBulkUpdate()
    {
        lock (_gate)
        {
            BulkUpdates++;
            _bulkDepth++;
        }

        return new BulkScope(this);
    }

    /// <summary>Raises <see cref="IndexChanged" /> with this snapshot, leaving <see cref="Current" /> as it is.</summary>
    public void Raise(GameIndex index)
    {
        IndexChanged?.Invoke(index);
    }

    /// <summary>Raises <see cref="LocalisationChanged" /> with this index, leaving <see cref="Current" /> as it is.</summary>
    public void RaiseLocalisation(ILocalisationIndex index)
    {
        LocalisationChanged?.Invoke(index);
    }

    /// <summary>Raises <see cref="DynamicEnumChanged" /> with this snapshot, leaving <see cref="Current" /> as it is.</summary>
    public void RaiseDynamicEnumChanged(GameIndex index)
    {
        DynamicEnumChanged?.Invoke(index);
    }

    /// <summary>
    ///     What an update leaves in the snapshot: a document with that version and no symbols, since
    ///     nothing was parsed. Silent, because nothing was indexed either; a test that wants the event
    ///     raises it.
    /// </summary>
    private void Stamp(string uri, int version)
    {
        _current = _current with
        {
            Documents = _current.Documents.SetItem(uri, new DocumentIndex(uri, version, [], []))
        };
    }

    private void Replace(GameIndex updated)
    {
        _current = updated;

        if (_bulkDepth > 0)
        {
            _bulkPending = true;
            return;
        }

        IndexChanged?.Invoke(_current);
    }

    private void EndBulk()
    {
        lock (_gate)
        {
            if (--_bulkDepth > 0 || !_bulkPending)
                return;

            _bulkPending = false;
            IndexChanged?.Invoke(_current);
        }
    }

    private sealed class BulkScope(FakeGameIndexService owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.EndBulk();
        }
    }
}