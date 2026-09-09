// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Persistence;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     One saved node position. The node is named by its thread and event here, in memory - the
///     file on disk holds the two hashed together and nothing else.
/// </summary>
public sealed record StoryLayoutEntry(string ThreadUri, string EventName, double X, double Y);

/// <summary>A node the caller is holding, and can therefore name a stored key with.</summary>
public sealed record StoryLayoutNode(string ThreadUri, string EventName);

/// <summary>Per campaign-faction story graph layout persistence.</summary>
public interface IStoryLayoutStore
{
    /// <summary>
    ///     The saved positions for a graph, for the nodes it actually has.
    ///     <para>
    ///         The nodes are passed in because the file names them by key: turning a key back into
    ///         a thread and an event is only possible against the candidates the caller is holding,
    ///         and that is deliberate - it is what stops the file from carrying either.
    ///     </para>
    /// </summary>
    IReadOnlyList<StoryLayoutEntry> Get(StoryModelKey key, IReadOnlyList<StoryLayoutNode> nodes);

    /// <summary>Upserts by node; entries not mentioned keep their stored position.</summary>
    void Set(StoryModelKey key, IReadOnlyList<StoryLayoutEntry> entries);
}

/// <summary>
///     JSON sidecar under the project's <c>.aetswg/</c> directory (<c>story-layout.json</c>), held
///     by a <see cref="SidecarStore{T}" />. Layout is editor state, not game data - it deliberately
///     lives next to the index caches, not in the mod's xml tree. Without a .pgproj the store
///     degrades to in-memory. Orphaned entries (deleted events) are harmless and left in place.
/// </summary>
public sealed class StoryLayoutStore : IStoryLayoutStore
{
    private readonly object _gate = new();
    private readonly ProjectDocumentKeys _keys;
    private readonly SidecarStore<StoryLayoutDocument.Payload> _store;

    public StoryLayoutStore(
        IModProjectReloadService reloadService,
        IFileHelper fileHelper,
        ILogger<StoryLayoutStore> logger)
    {
        _keys = new ProjectDocumentKeys(reloadService, fileHelper);
        _store = new SidecarStore<StoryLayoutDocument.Payload>(
            "story-layout.json", StoryLayoutDocument.TypeName, StoryLayoutDocument.Version,
            StoryLayoutDocument.MigrationsUsing(_keys.NodeKeyForBaseName, BucketKey),
            new AetswgSidecarLocator(reloadService), fileHelper, logger,
            () => new StoryLayoutDocument.Payload());
    }

    public IReadOnlyList<StoryLayoutEntry> Get(StoryModelKey key, IReadOnlyList<StoryLayoutNode> nodes)
    {
        lock (_gate)
        {
            var graphs = _store.Load().Value.Graphs;

            // Only the nodes this graph holds can be named, which is also the filter: an entry
            // whose event is not in the campaign any more simply does not come back.
            var byKey = new Dictionary<Guid, StoryLayoutNode>();
            foreach (var node in nodes)
                if (_keys.NodeKey(node.ThreadUri, node.EventName) is { } nodeKey)
                    byKey[nodeKey] = node;

            var stored = graphs.TryGetValue(GraphKeyOf(key), out var scoped)
                ? scoped
                // A sidecar written before layouts were faction-scoped keyed by the campaign alone.
                // Both factions read it once, which is right: an entry names a node, so each graph
                // picks up only the positions of nodes it actually has. The next Set writes the
                // scoped key and the old entry stops being consulted.
                : graphs.GetValueOrDefault(
                    ProjectDocumentKeys.LegacyGraphKey(key.Campaign).ToString()) ?? [];

            return stored
                .Where(e => byKey.ContainsKey(e.Key))
                .Select(e => new StoryLayoutEntry(
                    byKey[e.Key].ThreadUri, byKey[e.Key].EventName, e.X, e.Y))
                .ToList();
        }
    }

    public void Set(StoryModelKey key, IReadOnlyList<StoryLayoutEntry> entries)
    {
        lock (_gate)
        {
            var document = _store.Load().Value;
            if (!document.Graphs.TryGetValue(GraphKeyOf(key), out var existing))
                document.Graphs[GraphKeyOf(key)] = existing = [];

            foreach (var entry in entries)
            {
                // A thread outside the project has no key that survives a clone, so there is
                // nothing worth writing down for it.
                if (_keys.NodeKey(entry.ThreadUri, entry.EventName) is not { } nodeKey) continue;

                var index = existing.FindIndex(e => e.Key == nodeKey);
                var updated = new StoryLayoutDocument.Entry { Key = nodeKey, X = entry.X, Y = entry.Y };

                if (index >= 0) existing[index] = updated;
                else existing.Add(updated);
            }

            _store.TrySave(document, out _);
        }
    }

    /// <summary>
    ///     A version-zero bucket name to its key. Those buckets were <c>campaign/faction</c>, or the
    ///     campaign alone before layouts were faction-scoped; both are hashed as what they were.
    /// </summary>
    private static Guid BucketKey(string bucket)
    {
        var slash = bucket.IndexOf('/');
        return slash < 0
            ? ProjectDocumentKeys.LegacyGraphKey(bucket)
            : ProjectDocumentKeys.GraphKey(bucket[..slash], bucket[(slash + 1)..]);
    }

    private static string GraphKeyOf(StoryModelKey key)
    {
        return ProjectDocumentKeys.GraphKey(key.Campaign, key.Faction).ToString();
    }
}
