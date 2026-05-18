// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     Synchronous access to the in-memory tag registry loaded from the schema repository.
///     Loading and refresh are async; queries always hit an in-memory snapshot.
/// </summary>
public interface ISchemaProvider
{
    IReadOnlyList<XmlTagDefinition> AllTags { get; }
    IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes { get; }

    IReadOnlyList<EnumDefinition> AllEnums { get; }

    /// <summary>Returns the first definition found for this tag across all types. Use when context type is unknown.</summary>
    XmlTagDefinition? GetTag(string tagName);

    /// <summary>Returns every definition for this tag, one per KeyMapTable that declares it.</summary>
    IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName);

    GameObjectTypeDefinition? GetObjectType(string typeName);

    /// <summary>Returns all tags defined for the given KeyMapTable type name.</summary>
    IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName);

    /// <summary>Returns the enum definition for the given C++ enum name, or null if unknown.</summary>
    EnumDefinition? GetEnum(string enumName);

    /// <summary>Fired when the schema index has been refreshed from the source.</summary>
    event EventHandler? SchemaRefreshed;
}