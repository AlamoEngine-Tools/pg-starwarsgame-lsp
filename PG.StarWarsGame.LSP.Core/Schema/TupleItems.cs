// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>One item of a tuple value.</summary>
/// <param name="Text">The item, trimmed.</param>
/// <param name="Offset">Where the trimmed item starts in the raw value it was read from.</param>
/// <param name="SlotIndex">
///     Which slot it fills. In a <see cref="XmlValueType.TupleList" />, its position among the items
///     modulo the group size; in a <see cref="XmlValueType.ListMap" />, 0 for a key and 1 for an
///     item of one.
/// </param>
/// <param name="Slot">That slot's definition, or null for a tag that declares none.</param>
public sealed record TupleItem(string Text, int Offset, int SlotIndex, TupleSlotDefinition? Slot);

/// <summary>
///     Reads a tuple value into its items. Validation, references, hover and completion all read the
///     value this one way, so they cannot disagree about which item is the model.
/// </summary>
public static class TupleItems
{
    /// <summary>
    ///     The value's items in order. Empty items are skipped - the shipped lists end on a comma,
    ///     and an item that says nothing fills no slot - so a slot is a position among the items
    ///     that are there, the same count the shape check makes.
    /// </summary>
    /// <param name="tag">The tag whose slots and value type say how to read the value.</param>
    /// <param name="raw">The value as written, untrimmed, so the offsets stay true to it.</param>
    /// <param name="isKey">
    ///     For a <see cref="XmlValueType.ListMap" />: which items are keys - see
    ///     <see cref="ListMapKeys" />. Omitted, the schema decides where it can; where it cannot (an
    ///     object key, with no index to look it up in) the first item is the key and the rest are
    ///     its items.
    /// </param>
    public static IReadOnlyList<TupleItem> Read(XmlTagDefinition tag, string raw, Func<string, bool>? isKey = null)
    {
        var items = new List<TupleItem>();
        var groupSize = GroupSize(tag);
        var isListMap = tag.ValueType == XmlValueType.ListMap && tag.Slots.Count == 2;
        if (isListMap) isKey ??= ListMapKeys.FromSchema(tag);
        var start = 0;

        while (start <= raw.Length)
        {
            var comma = raw.IndexOf(',', start);
            var end = comma < 0 ? raw.Length : comma;
            var segment = raw[start..end];
            var text = segment.Trim();

            if (text.Length > 0)
            {
                var slotIndex = !isListMap ? items.Count % groupSize
                    : isKey is not null ? isKey(text) ? 0 : 1
                    : items.Count == 0 ? 0 : 1;
                var offset = start + segment.IndexOf(text, StringComparison.Ordinal);
                items.Add(new TupleItem(text, offset, slotIndex,
                    tag.Slots.Count > 0 ? tag.Slots[slotIndex] : null));
            }

            if (comma < 0) break;
            start = comma + 1;
        }

        return items;
    }

    /// <summary>How many items make one group: the slot count, or a pair for a tag that declares none.</summary>
    public static int GroupSize(XmlTagDefinition tag)
    {
        return tag.Slots.Count > 0 ? tag.Slots.Count : 2;
    }

    /// <summary>The value's shape in words, for hover and diagnostics: <c>`Terrain, Model` pairs</c>.</summary>
    public static string Describe(XmlTagDefinition tag)
    {
        if (tag.Slots.Count == 0) return "comma-separated pairs";

        if (tag.ValueType == XmlValueType.ListMap && tag.Slots.Count == 2)
            return $"`{tag.Slots[0].Label}` keys, each followed by `{tag.Slots[1].Label}` items";

        var labels = string.Join(", ", tag.Slots.Select(s => s.Label));
        return tag.Slots.Count == 2 ? $"`{labels}` pairs" : $"`{labels}` groups";
    }
}