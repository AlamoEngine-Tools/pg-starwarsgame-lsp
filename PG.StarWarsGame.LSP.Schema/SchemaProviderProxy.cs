// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Schema;

/// <summary>
///     A late-binding proxy for <see cref="ISchemaProvider" /> that allows the real provider
///     (local or HTTP) to be selected in <c>OnInitialize</c> after LSP initialization options
///     are known, while still satisfying DI resolution that occurs during server startup.
/// </summary>
public sealed class SchemaProviderProxy : LateBindingProxy<ISchemaProvider>, ISchemaProvider
{
    private readonly TaskCompletionSource _readyTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Subscribers are stored here so they survive the _inner swap in Configure().
    // The old forwarding pattern (add => _inner.SchemaRefreshed += value) silently
    // subscribed to the empty placeholder, meaning events from the real provider
    // (HTTP/local) were never delivered to downstream subscribers.
    private EventHandler? _schemaRefreshed;

    public SchemaProviderProxy() : base(SchemaIndex.EmptyProvider)
    {
    }

    public Task ReadyAsync => _readyTcs.Task;

    public event EventHandler? SchemaRefreshed
    {
        add => _schemaRefreshed += value;
        remove => _schemaRefreshed -= value;
    }

    public XmlTagDefinition? GetTag(string tagName)
    {
        return Inner.GetTag(tagName);
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
    {
        return Inner.GetAllTagDefinitions(tagName);
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => Inner.AllTags;

    public GameObjectTypeDefinition? GetObjectType(string typeName)
    {
        return Inner.GetObjectType(typeName);
    }

    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => Inner.AllObjectTypes;

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        return Inner.GetTagsForType(typeName);
    }

    public EnumDefinition? GetEnum(string enumName)
    {
        return Inner.GetEnum(enumName);
    }

    public IReadOnlyList<EnumDefinition> AllEnums => Inner.AllEnums;
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => Inner.AllHardcodedSets;
    public IReadOnlyList<MetafileDefinition> AllMetafiles => Inner.AllMetafiles;

    protected override void OnConfigured(ISchemaProvider inner)
    {
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