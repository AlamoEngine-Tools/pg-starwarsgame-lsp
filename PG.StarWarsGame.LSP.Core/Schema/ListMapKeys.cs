// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     Whether an item of a <see cref="XmlValueType.ListMap" /> value is a KEY - the test the engine
///     makes on every item before it adds one to the current key's list.
/// </summary>
/// <remarks>
///     <para>
///         Measured in the engine: each item is first looked up in the key's own table - the factions
///         alone for a faction key, the animation states alone for an animation key - by its whole
///         name, ends trimmed and case ignored. Found, it starts a new group; not found, it joins the
///         last one. Where it sits plays no part, so an object named like a key opens a group of its
///         own.
///     </para>
///     <para>
///         An enum key is answered from the schema. An object key needs the index, which the parser
///         does not have - it builds the index - so <see cref="FromSchema" /> answers null there
///         and the caller reads positionally.
///     </para>
/// </remarks>
public static class ListMapKeys
{
    /// <summary>The key test from the schema alone, or null when the key slot needs the index.</summary>
    public static Func<string, bool>? FromSchema(XmlTagDefinition tag)
    {
        if (KeySlot(tag) is not { ReferenceKind: ReferenceKind.Enum, Enum: { } keyEnum })
            return null;

        var names = new HashSet<string>(keyEnum.Values.Select(v => v.Name.Trim()), StringComparer.OrdinalIgnoreCase);
        return item => names.Contains(item.Trim());
    }

    /// <summary>The key test with the index to look an object key up in.</summary>
    public static Func<string, bool> FromIndex(XmlTagDefinition tag, GameIndex index)
    {
        if (FromSchema(tag) is { } fromSchema)
            return fromSchema;

        if (KeySlot(tag) is not { ReferenceKind: ReferenceKind.XmlObject } slot)
            return _ => false;

        // The engine's key table holds one type and nothing else, so an object of another type that
        // shares the name is no key - it is looked up as an item instead.
        var keyType = slot.ObjectType?.TypeName ?? slot.ReferenceTypeName;
        return item => index.ResolveAll(item.Trim()).Any(symbol =>
            slot.Kind is not null
                ? ObjectKinds.Match(index, symbol, slot.Kind) == KindMatch.Yes
                : string.Equals(symbol.TypeName, keyType, StringComparison.OrdinalIgnoreCase));
    }

    private static TupleSlotDefinition? KeySlot(XmlTagDefinition tag)
    {
        return tag.ValueType == XmlValueType.ListMap && tag.Slots.Count > 0 ? tag.Slots[0] : null;
    }
}