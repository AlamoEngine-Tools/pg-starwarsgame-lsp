// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema;

/// <summary>Immutable in-memory snapshot of the schema repository with all cross-references fully resolved.</summary>
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
    private readonly Dictionary<string, ObjectKindDefinition> _kinds;

    internal SchemaIndex(
        IEnumerable<(string typeName, IReadOnlyList<RawTagDefinition> tags)> tagsByType,
        IEnumerable<GameObjectTypeDefinition> types,
        IEnumerable<RawEnumDefinition> enums,
        IEnumerable<HardcodedReferenceSet>? hardcodedSets = null,
        IEnumerable<MetafileDefinition>? metafiles = null,
        IEnumerable<ObjectKindDefinition>? kinds = null)
    {
        _tags = new Dictionary<string, XmlTagDefinition>(StringComparer.OrdinalIgnoreCase);
        _tagsByType = new Dictionary<string, IReadOnlyList<XmlTagDefinition>>(StringComparer.OrdinalIgnoreCase);
        _allByTagName = new Dictionary<string, List<XmlTagDefinition>>(StringComparer.OrdinalIgnoreCase);
        _types = new Dictionary<string, GameObjectTypeDefinition>(StringComparer.OrdinalIgnoreCase);
        _enums = new Dictionary<string, EnumDefinition>(StringComparer.OrdinalIgnoreCase);
        _kinds = new Dictionary<string, ObjectKindDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in types)
            _types[type.TypeName] = type;

        // A kind is a predicate over an object's content, resolved by the index at query time; the
        // schema only carries the definitions, so nothing here cross-references them yet.
        foreach (var kind in kinds ?? [])
            _kinds[kind.Kind] = kind;

        AllHardcodedSets = hardcodedSets?.ToArray() ?? [];
        var hardcodedByName = AllHardcodedSets.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        // Phase 1: resolve enums (params may reference types - types are already indexed above).
        // A param may also name another ENUM (a story event's filter or compare method), which is
        // resolved through _enums and so has to be there already: the first pass registers every
        // enum, the second resolves the params against the complete set, whatever the file order.
        var rawEnums = enums.ToList();
        foreach (var rawEnum in rawEnums)
            _enums[rawEnum.Name] = ResolveEnum(rawEnum);
        foreach (var rawEnum in rawEnums)
            _enums[rawEnum.Name] = ResolveEnum(rawEnum);

        // Phase 2: resolve tags (may reference types, hardcoded sets, and enums - all indexed above)
        foreach (var (typeName, rawTags) in tagsByType)
        {
            var resolved = rawTags.Select(raw => ResolveTag(raw, hardcodedByName)).ToArray();
            foreach (var tag in resolved)
            {
                _tags.TryAdd(tag.Tag, tag);
                if (!_allByTagName.TryGetValue(tag.Tag, out var all))
                    _allByTagName[tag.Tag] = all = [];
                all.Add(tag);
            }

            _tagsByType[typeName] = resolved;
        }

        AllTags = [.. _tags.Values];
        AllObjectTypes = [.. _types.Values];
        AllEnums = [.. _enums.Values];
        AllMetafiles = metafiles?.ToArray() ?? [];
        AllKinds = [.. _kinds.Values];
    }

    public IReadOnlyList<XmlTagDefinition> AllTags { get; }

    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes { get; }

    public IReadOnlyList<ObjectKindDefinition> AllKinds { get; }

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

    public ObjectKindDefinition? GetKind(string kindName)
    {
        return _kinds.TryGetValue(kindName, out var def) ? def : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        return _tagsByType.TryGetValue(typeName, out var list) ? list : [];
    }

    public EnumDefinition? GetEnum(string enumName)
    {
        return _enums.TryGetValue(enumName, out var def) ? def : null;
    }

    private XmlTagDefinition ResolveTag(RawTagDefinition raw, Dictionary<string, HardcodedReferenceSet> hardcodedByName)
    {
        return new XmlTagDefinition
        {
            Tag = raw.Tag,
            ValueType = raw.ValueType,
            ReferenceKind = raw.ReferenceKind,
            ReferenceTypeName = raw.ReferenceType,
            ObjectType = raw.ReferenceKind == ReferenceKind.XmlObject && raw.ReferenceType is not null
                ? _types.GetValueOrDefault(raw.ReferenceType)
                : null,
            HardcodedSet = raw.ReferenceKind == ReferenceKind.HardcodedSet && raw.ReferenceType is not null
                ? hardcodedByName.GetValueOrDefault(raw.ReferenceType)
                : null,
            Enum = raw.ReferenceKind == ReferenceKind.Enum && raw.EnumName is not null
                ? _enums.GetValueOrDefault(raw.EnumName)
                : null,
            // A kind and a type never share a name, so this is unambiguous: whichever one the
            // referenceType resolves to is what the slot accepts.
            Kind = raw.ReferenceType is not null ? _kinds.GetValueOrDefault(raw.ReferenceType) : null,
            SemanticType = raw.SemanticType,
            ValueGroups = raw.ValueGroups,
            AllowedValues = raw.AllowedValues,
            Deprecated = raw.Deprecated,
            AvailableSince = raw.AvailableSince,
            Description = raw.Description,
            Notes = raw.Notes,
            MultipleAllowed = raw.MultipleAllowed,
            VariantMode = raw.VariantMode,
            ValidationOverride = raw.ValidationOverride
        };
    }

    private EnumDefinition ResolveEnum(RawEnumDefinition raw)
    {
        var values = raw.Values.Select(rawVal => new EnumValueDefinition
        {
            Name = rawVal.Name,
            Description = rawVal.Description,
            Notes = rawVal.Notes,
            Deprecated = rawVal.Deprecated,
            Untested = rawVal.Untested,
            AvailableSince = rawVal.AvailableSince,
            Groups = rawVal.Groups,
            Params = rawVal.Params?.Select(ResolveParam).ToList()
        }).ToList();

        return new EnumDefinition
        {
            Name = raw.Name,
            Kind = raw.Kind,
            IsBitfield = raw.IsBitfield,
            SourceFile = raw.SourceFile,
            Description = raw.Description,
            Notes = raw.Notes,
            Deprecated = raw.Deprecated,
            AvailableSince = raw.AvailableSince,
            Values = values
        };
    }

    private ParamDefinition ResolveParam(RawParamDefinition raw)
    {
        // A param declares its enum by name (DynamicEnumValue + enumName) and its object type by
        // name (NameReference + referenceType), with no referenceKind: the kind follows from what
        // the name resolves to. A referenceType that is no types.yaml type (StoryFlag, StoryEventName)
        // keeps its name, its kind and a null ObjectType - the story graph keys edges on the name.
        var objectType = raw.ReferenceType is not null ? _types.GetValueOrDefault(raw.ReferenceType) : null;
        var enumDef = raw.EnumName is not null ? _enums.GetValueOrDefault(raw.EnumName) : null;
        // A referenceType naming a KIND references an object just as much as one naming a type -
        // 58 story slots ask for a planet this way - so it resolves to XmlObject too.
        var objectKind = raw.ReferenceType is not null ? _kinds.GetValueOrDefault(raw.ReferenceType) : null;
        var kind = raw.ReferenceKind != ReferenceKind.None ? raw.ReferenceKind
            : enumDef is not null ? ReferenceKind.Enum
            : objectType is not null || objectKind is not null ? ReferenceKind.XmlObject
            : ReferenceKind.None;
        return new ParamDefinition
        {
            Position = raw.Position,
            ValueType = raw.ValueType,
            ReferenceKind = kind,
            ReferenceTypeName = raw.ReferenceType,
            ObjectType = objectType,
            Kind = objectKind,
            Enum = enumDef,
            Optional = raw.Optional,
            Label = raw.Label,
            Description = raw.Description,
            Notes = raw.Notes
        };
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