// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Schema.Providers;

/// <summary>
///     A provider that answers every query from one in-memory <see cref="SchemaIndex" /> snapshot.
/// </summary>
/// <remarks>
///     <para>
///         The loading strategies differ completely - one fetches over HTTP with ETags and a disk
///         cache, the other walks a directory and hot-reloads on a file watcher - but once a load
///         finishes they both do the same thing: swap in a new index and answer thirteen members
///         straight out of it. That block was duplicated verbatim in both, so a member added to
///         <see cref="ISchemaProvider" /> had to be written twice and a provider that missed one
///         still compiled, because the interface's newer members carry default implementations
///         returning empty.
///     </para>
///     <para>
///         Deliberately not shared with <c>SchemaProviderProxy</c>. That one forwards to another
///         PROVIDER which is swapped at initialisation, not to an index snapshot, and its event has
///         to survive the swap - the same shape on the surface, a different thing underneath.
///     </para>
/// </remarks>
public abstract class SchemaIndexProviderBase : ISchemaProvider
{
    private volatile SchemaIndex _current = SchemaIndex.Empty;

    /// <summary>
    ///     The snapshot in force. Never null, empty before the first load, and replaced wholesale -
    ///     so a reader either sees the old index or the new one, never a half-built one.
    /// </summary>
    protected SchemaIndex Current => _current;

    public event EventHandler? SchemaRefreshed;

    public XmlTagDefinition? GetTag(string tagName)
    {
        return _current.GetTag(tagName);
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
    {
        return _current.GetAllTagDefinitions(tagName);
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => _current.AllTags;

    public GameObjectTypeDefinition? GetObjectType(string typeName)
    {
        return _current.GetObjectType(typeName);
    }

    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => _current.AllObjectTypes;

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        return _current.GetTagsForType(typeName);
    }

    public EnumDefinition? GetEnum(string enumName)
    {
        return _current.GetEnum(enumName);
    }

    public IReadOnlyList<EnumDefinition> AllEnums => _current.AllEnums;

    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => _current.AllHardcodedSets;

    public IReadOnlyList<MetafileDefinition> AllMetafiles => _current.AllMetafiles;

    public IReadOnlyList<ObjectKindDefinition> AllKinds => _current.AllKinds;

    public ObjectKindDefinition? GetKind(string kindName)
    {
        return _current.GetKind(kindName);
    }

    /// <summary>
    ///     Publishes a newly loaded index and tells subscribers.
    /// </summary>
    /// <remarks>
    ///     The swap and the notification belong together: a subscriber that ran before the swap would
    ///     re-read the index it was told had changed and get the old one. C# also only lets an event
    ///     be raised by the class declaring it, so a derived provider could not do this itself.
    /// </remarks>
    protected void Publish(SchemaIndex index)
    {
        _current = index;
        SchemaRefreshed?.Invoke(this, EventArgs.Empty);
    }
}
