// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     One item of a repeating tuple value: <c>Land_Terrain_Model_Mapping</c> is a terrain, then a
///     model, again and again. Typed the way a whole tag is typed, or untyped - read as text and
///     checked for nothing.
/// </summary>
public sealed record TupleSlotDefinition
{
    /// <summary>What the item is, as the reader sees it: Terrain, Model, Music event.</summary>
    public required string Label { get; init; }

    public ReferenceKind ReferenceKind { get; init; }

    /// <summary>The raw <c>referenceType</c> from the schema, kept even when it names no known type.</summary>
    public string? ReferenceTypeName { get; init; }

    /// <summary>Non-null when <see cref="ReferenceKind" /> is XmlObject - the resolved target type.</summary>
    public GameObjectTypeDefinition? ObjectType { get; init; }

    /// <summary>Non-null when <see cref="ReferenceKind" /> is Enum - the resolved enum.</summary>
    public EnumDefinition? Enum { get; init; }

    /// <summary>
    ///     Non-null when the <c>referenceType</c> names an object KIND rather than a type: the slot
    ///     then accepts what an object IS, judged by its behaviours, as
    ///     <see cref="XmlTagDefinition.Kind" /> does for a whole tag.
    /// </summary>
    public ObjectKindDefinition? Kind { get; init; }
}