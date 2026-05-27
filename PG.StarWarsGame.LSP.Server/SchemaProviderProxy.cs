// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     A late-binding proxy for <see cref="ISchemaProvider" /> that allows the real provider
///     (local or HTTP) to be selected in <c>OnInitialize</c> after LSP initialization options
///     are known, while still satisfying DI resolution that occurs during server startup.
/// </summary>
internal sealed class SchemaProviderProxy : ISchemaProvider
{
    private readonly TaskCompletionSource _readyTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ISchemaProvider _inner = SchemaIndex.EmptyProvider;

    // Subscribers are stored here so they survive the _inner swap in Configure().
    // The old forwarding pattern (add => _inner.SchemaRefreshed += value) silently
    // subscribed to the empty placeholder, meaning events from the real provider
    // (HTTP/local) were never delivered to the WorkspaceScanner.
    private EventHandler? _schemaRefreshed;

    public Task ReadyAsync => _readyTcs.Task;

    public event EventHandler? SchemaRefreshed
    {
        add => _schemaRefreshed += value;
        remove => _schemaRefreshed -= value;
    }

    public XmlTagDefinition? GetTag(string tagName)
    {
        return _inner.GetTag(tagName);
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
    {
        return _inner.GetAllTagDefinitions(tagName);
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => _inner.AllTags;

    public GameObjectTypeDefinition? GetObjectType(string typeName)
    {
        return _inner.GetObjectType(typeName);
    }

    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => _inner.AllObjectTypes;

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        return _inner.GetTagsForType(typeName);
    }

    public EnumDefinition? GetEnum(string enumName)
    {
        return _inner.GetEnum(enumName);
    }

    public IReadOnlyList<EnumDefinition> AllEnums => _inner.AllEnums;
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => _inner.AllHardcodedSets;
    public IReadOnlyList<MetafileDefinition> AllMetafiles => _inner.AllMetafiles;

    /// <summary>
    ///     Called from <c>OnInitialize</c> once the schema source configuration is known.
    ///     Replaces the placeholder inner provider and signals <see cref="ISchemaProvider.ReadyAsync" />.
    /// </summary>
    public void Configure(ISchemaProvider inner)
    {
        _inner = inner;
        // Forward SchemaRefreshed from the real provider to our stored delegates.
        inner.SchemaRefreshed += (s, e) => _schemaRefreshed?.Invoke(s, e);
        // Chain: ready when the inner provider is ready.
        _ = inner.ReadyAsync.ContinueWith(_ => _readyTcs.TrySetResult(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        // If inner is already complete (LocalFileSchemaProvider), signal immediately.
        if (inner.ReadyAsync.IsCompleted)
            _readyTcs.TrySetResult();
    }
}