// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>
///     The schema <see cref="SchemaProviderBuilder" /> builds: lookups keyed like the real index,
///     <see cref="Add(XmlTagDefinition)" /> and its overloads for a test that registers an entry after
///     the code under test is wired, and <see cref="Refresh" /> for a test of something that listens
///     for a reload.
/// </summary>
/// <remarks>
///     Names compare case-insensitively. The FIRST declaration of a tag name answers
///     <c>GetTag</c>, every declaration is listed by <c>GetAllTagDefinitions</c>, and a type's tags are
///     the ones declared on it through <see cref="AddForType" />. Adding a type, enum, kind or set
///     under a name already registered replaces the earlier one, as a dictionary would.
/// </remarks>
public sealed class FakeSchemaProvider : ISchemaProvider
{
    private readonly Dictionary<string, List<XmlTagDefinition>> _allByTagName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EnumDefinition> _enums = [];
    private readonly Dictionary<string, EnumDefinition> _enumsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<XmlTagDefinition> _firstDeclarations = [];
    private readonly List<HardcodedReferenceSet> _hardcodedSets = [];
    private readonly List<ObjectKindDefinition> _kinds = [];
    private readonly Dictionary<string, ObjectKindDefinition> _kindsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MetafileDefinition> _metafiles = [];
    private readonly List<ScannedDirectoryDefinition> _scannedDirectories = [];
    private readonly Dictionary<string, XmlTagDefinition> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<XmlTagDefinition>> _tagsByType = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GameObjectTypeDefinition> _types = [];
    private readonly Dictionary<string, GameObjectTypeDefinition> _typesByName = new(StringComparer.OrdinalIgnoreCase);

    internal FakeSchemaProvider(Task ready)
    {
        ReadyAsync = ready;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => _firstDeclarations;
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => _types;
    public IReadOnlyList<EnumDefinition> AllEnums => _enums;
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => _hardcodedSets;
    public IReadOnlyList<MetafileDefinition> AllMetafiles => _metafiles;
    public IReadOnlyList<ScannedDirectoryDefinition> AllScannedDirectories => _scannedDirectories;
    public IReadOnlyList<ObjectKindDefinition> AllKinds => _kinds;
    public Task ReadyAsync { get; }

    public XmlTagDefinition? GetTag(string tagName)
    {
        return _tags.GetValueOrDefault(tagName);
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
    {
        return _allByTagName.TryGetValue(tagName, out var all) ? all : [];
    }

    public GameObjectTypeDefinition? GetObjectType(string typeName)
    {
        return _typesByName.GetValueOrDefault(typeName);
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        return _tagsByType.TryGetValue(typeName, out var tags) ? tags : [];
    }

    public EnumDefinition? GetEnum(string enumName)
    {
        return _enumsByName.GetValueOrDefault(enumName);
    }

    public ObjectKindDefinition? GetKind(string kindName)
    {
        return _kindsByName.GetValueOrDefault(kindName);
    }

    public event EventHandler? SchemaRefreshed;

    /// <summary>Raises <see cref="SchemaRefreshed" />, as a reload of the schema files would.</summary>
    public void Refresh()
    {
        SchemaRefreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Declares a tag on no particular type: found by name, listed for no type.</summary>
    public FakeSchemaProvider Add(XmlTagDefinition tag)
    {
        if (_tags.TryAdd(tag.Tag, tag))
            _firstDeclarations.Add(tag);

        if (!_allByTagName.TryGetValue(tag.Tag, out var all))
            _allByTagName[tag.Tag] = all = [];
        all.Add(tag);
        return this;
    }

    /// <summary>Declares tags on one type, in this order: found by name and listed for the type.</summary>
    public FakeSchemaProvider AddForType(string typeName, params XmlTagDefinition[] tags)
    {
        if (!_tagsByType.TryGetValue(typeName, out var list))
            _tagsByType[typeName] = list = [];

        foreach (var tag in tags)
        {
            Add(tag);
            list.Add(tag);
        }

        return this;
    }

    public FakeSchemaProvider Add(GameObjectTypeDefinition type)
    {
        Put(_types, _typesByName, type.TypeName, type);
        return this;
    }

    public FakeSchemaProvider Add(EnumDefinition definition)
    {
        Put(_enums, _enumsByName, definition.Name, definition);
        return this;
    }

    public FakeSchemaProvider Add(ObjectKindDefinition kind)
    {
        Put(_kinds, _kindsByName, kind.Kind, kind);
        return this;
    }

    public FakeSchemaProvider Add(HardcodedReferenceSet set)
    {
        var existing = _hardcodedSets.FindIndex(s => s.Name.Equals(set.Name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
            _hardcodedSets[existing] = set;
        else
            _hardcodedSets.Add(set);
        return this;
    }

    public FakeSchemaProvider Add(MetafileDefinition metafile)
    {
        _metafiles.Add(metafile);
        return this;
    }

    public FakeSchemaProvider Add(ScannedDirectoryDefinition directory)
    {
        _scannedDirectories.Add(directory);
        return this;
    }

    private static void Put<T>(List<T> list, Dictionary<string, T> byName, string name, T item)
    {
        if (byName.TryGetValue(name, out var earlier))
            list[list.IndexOf(earlier)] = item;
        else
            list.Add(item);

        byName[name] = item;
    }
}