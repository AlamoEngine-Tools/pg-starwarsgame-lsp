// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>
///     A schema that knows nothing. For handlers that take <see cref="ISchemaProvider" /> only to
///     pass it on to a collaborator whose schema-dependent behaviour is covered by that
///     collaborator's own tests.
///
///     Not sealed, and <see cref="GetTag" /> and <see cref="GetObjectType" /> are virtual: a test
///     that needs the resolver to see ONE tag's real schema mode - <c>Death_Clone</c> is merge plus
///     multipleAllowed, and reporting no schema makes the resolver collapse it - or that needs a
///     handful of type names to be recognised as real object types, overrides that alone rather than
///     restating the whole interface.
/// </summary>
internal class NullSchemaProvider : ISchemaProvider
{
    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public virtual XmlTagDefinition? GetTag(string tagName)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
    {
        return [];
    }

    public virtual GameObjectTypeDefinition? GetObjectType(string typeName)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string enumName)
    {
        return null;
    }

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}
