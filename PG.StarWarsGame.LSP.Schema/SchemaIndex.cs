// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Schema;

/// <summary>Immutable in-memory snapshot of the schema repository.</summary>
public sealed class SchemaIndex
{
    public static readonly SchemaIndex Empty = new([], [], []);

    /// <summary>An <see cref="ISchemaProvider" /> that returns empty collections for all queries and is always ready.</summary>
    public static readonly ISchemaProvider EmptyProvider = new EmptySchemaProviderImpl();

    private readonly Dictionary<string, List<XmlTagDefinition>> _allByTagName;
    private readonly Dictionary<string, EnumDefinition> _enums;
    private readonly Dictionary<string, XmlTagDefinition> _tags;
    private readonly Dictionary<string, IReadOnlyList<XmlTagDefinition>> _tagsByType;
    private readonly Dictionary<string, GameObjectTypeDefinition> _types;

    public SchemaIndex(
        IEnumerable<(string typeName, IReadOnlyList<XmlTagDefinition> tags)> tagsByType,
        IEnumerable<GameObjectTypeDefinition> types,
        IEnumerable<EnumDefinition> enums,
        IEnumerable<HardcodedReferenceSet>? hardcodedSets = null,
        IEnumerable<MetafileDefinition>? metafiles = null)
    {
        _tags = new Dictionary<string, XmlTagDefinition>(StringComparer.OrdinalIgnoreCase);
        _tagsByType = new Dictionary<string, IReadOnlyList<XmlTagDefinition>>(StringComparer.OrdinalIgnoreCase);
        _allByTagName = new Dictionary<string, List<XmlTagDefinition>>(StringComparer.OrdinalIgnoreCase);
        _types = new Dictionary<string, GameObjectTypeDefinition>(StringComparer.OrdinalIgnoreCase);
        _enums = new Dictionary<string, EnumDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var (typeName, tags) in tagsByType)
        {
            foreach (var tag in tags)
            {
                _tags.TryAdd(tag.Tag, tag);
                if (!_allByTagName.TryGetValue(tag.Tag, out var all))
                    _allByTagName[tag.Tag] = all = [];
                all.Add(tag);
            }

            _tagsByType[typeName] = tags;
        }

        foreach (var type in types)
            _types[type.TypeName] = type;

        foreach (var e in enums)
            _enums[e.Name] = e;

        AllTags = [.. _tags.Values];
        AllObjectTypes = [.. _types.Values];
        AllEnums = [.. _enums.Values];
        AllHardcodedSets = hardcodedSets?.ToArray() ?? [];
        AllMetafiles = metafiles?.ToArray() ?? [];
    }

    public IReadOnlyList<XmlTagDefinition> AllTags { get; }

    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes { get; }

    public IReadOnlyList<EnumDefinition> AllEnums { get; }

    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets { get; }

    public IReadOnlyList<MetafileDefinition> AllMetafiles { get; }

    public XmlTagDefinition? GetTag(string tagName)
    {
        return _tags.TryGetValue(tagName, out var def) ? def : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
    {
        return _allByTagName.TryGetValue(tagName, out var list) ? list.AsReadOnly() : [];
    }

    public GameObjectTypeDefinition? GetObjectType(string typeName)
    {
        return _types.TryGetValue(typeName, out var def) ? def : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        return _tagsByType.TryGetValue(typeName, out var list) ? list : [];
    }

    public EnumDefinition? GetEnum(string enumName)
    {
        return _enums.TryGetValue(enumName, out var def) ? def : null;
    }

    private sealed class EmptySchemaProviderImpl : ISchemaProvider
    {
        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public XmlTagDefinition? GetTag(string tagName)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string enumName)
        {
            return null;
        }

        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
    }
}