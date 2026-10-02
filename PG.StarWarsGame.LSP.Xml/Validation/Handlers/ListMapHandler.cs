// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reads every occurrence of a <see cref="XmlValueType.ListMap" /> tag on one object the way the
///     engine does, and says where that is not what the author meant.
/// </summary>
/// <remarks>
///     <para>
///         Measured in the engine: each item is tried as a key first (see <see cref="ListMapKeys" />)
///         and otherwise added to the last key's list. A value whose first item is no key is dropped
///         whole. Outside a variant a repeated tag appends to the same list; inside one, every
///         occurrence clears it first, so only the last survives. Nothing is merged or deduplicated,
///         and an item may be any object at all.
///     </para>
///     <para>
///         Object items are not checked for existence here - the parser records them as references and
///         the reference pipeline owns that. Asset and enum values are checked by the same
///         <see cref="SlotItemChecks" /> a tuple item gets.
///     </para>
/// </remarks>
public sealed class ListMapHandler : XmlDiagnosticsHandler<XmlListMapFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.PerFactionObjectList;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlListMapFact fact, DiagnosticsContext ctx)
    {
        var tag = fact.Tag;
        if (tag.Slots.Count != 2 || fact.Occurrences.Count == 0) return [];

        var keySlot = tag.Slots[0];
        var valueSlot = tag.Slots[1];
        var keyType = keySlot.ObjectType?.TypeName ?? keySlot.ReferenceTypeName;

        // An object key is looked up in the index. With nothing of its type indexed - startup, or no
        // factions loaded - every key would read as "none", which is the tool's state, not the data.
        if (keySlot.ReferenceKind == ReferenceKind.XmlObject &&
            (keyType is null || !ctx.Index.IndexedTypeNames.Contains(keyType)))
            return [];

        var isKey = ListMapKeys.FromIndex(tag, ctx.Index);
        var results = new List<XmlDiagnosticResult>();

        // In a variant every occurrence clears the list, so all but the last are dead text.
        var read = fact.Occurrences;
        if (fact.IsVariant && fact.Occurrences.Count > 1)
        {
            var last = fact.Occurrences[^1];
            foreach (var dead in fact.Occurrences.Take(fact.Occurrences.Count - 1))
                results.Add(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"A variant reads only its last <{tag.Tag}>, on line {last.Line + 1}. Each one replaces the list.",
                    dead.Line, dead.Column, dead.Length, Id: DiagnosticIds.ListMapReplacedInVariant));
            read = [last];
        }

        string? currentKey = null;
        var keysSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var occurrence in read)
        {
            for (var i = 0; i < occurrence.Items.Count; i++)
            {
                var item = occurrence.Items[i];

                if (isKey(item.Text))
                {
                    if (currentKey is not null && NamesAnObject(item.Text, keySlot, keyType, ctx.Index))
                        results.Add(At(item, XmlDiagnosticSeverity.Warning,
                            $"'{item.Text}' is {Article(keySlot.Label)} {Describe(keySlot)} as well as an object, so the engine starts a new group with it instead of adding it to '{currentKey}'.",
                            DiagnosticIds.ListMapItemNamesAKey));

                    if (keysSeen.TryGetValue(item.Text, out var firstLine))
                        results.Add(At(item, XmlDiagnosticSeverity.Warning,
                            $"'{item.Text}' already starts a group of <{tag.Tag}> on line {firstLine + 1}. The engine keeps the groups apart and does not merge them.",
                            DiagnosticIds.ListMapRepeatedKey));
                    else
                        keysSeen[item.Text] = item.Line;

                    currentKey = item.Text;
                    continue;
                }

                if (currentKey is null)
                {
                    // The engine stops reading the value here and keeps nothing of it.
                    results.Add(At(item, XmlDiagnosticSeverity.Error,
                        $"<{tag.Tag}> has to start with {Article(keySlot.Label)} {Describe(keySlot)}: '{item.Text}' is none. The engine drops the whole value.",
                        DiagnosticIds.PerFactionObjectListUnknownFaction));
                    break;
                }

                if (i == 0)
                    results.Add(At(item, XmlDiagnosticSeverity.Warning,
                        $"<{tag.Tag}> starts with '{item.Text}', which is no {Describe(keySlot)}, so its items join the key '{currentKey}' of the previous <{tag.Tag}>.",
                        DiagnosticIds.ListMapContinuesPreviousKey));

                if (ValueKindProblem(item.Text, tag, valueSlot, ctx.Index) is { } kindMessage)
                    results.Add(At(item, XmlDiagnosticSeverity.Warning, kindMessage, DiagnosticIds.ListMapValueKind));

                if ((SlotItemChecks.Enum(tag, valueSlot, item.Text, ctx) ??
                     SlotItemChecks.Asset(tag, valueSlot, item.Text, ctx))
                    is { } itemResult)
                    results.Add(itemResult with
                    {
                        OverrideLine = item.Line, OverrideColumn = item.Column, OverrideLength = item.Text.Length
                    });
            }

            // Inside a variant the next occurrence starts over; outside one it continues this list.
            if (fact.IsVariant) currentKey = null;
        }

        return results;
    }

    /// <summary>Whether a key's name is also an object the author could have meant as an item.</summary>
    private static bool NamesAnObject(string name, TupleSlotDefinition keySlot, string? keyType, GameIndex index)
    {
        return index.ResolveAll(name).Any(symbol =>
            keySlot.ReferenceKind != ReferenceKind.XmlObject ||
            !string.Equals(symbol.TypeName, keyType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     A value that resolves to an object of another kind or type than its slot names, or null.
    ///     Unresolved values are the reference pipeline's to report.
    /// </summary>
    private static string? ValueKindProblem(string value, XmlTagDefinition tag, TupleSlotDefinition slot,
        GameIndex index)
    {
        if (slot.ReferenceKind != ReferenceKind.XmlObject || index.Resolve(value) is not { } resolved)
            return null;

        if (slot.Kind is { } kind)
            return ObjectKinds.Match(index, resolved, kind) == KindMatch.No
                ? $"'{value}' is not {Article(kind.Kind)} {kind.Kind}. The engine accepts any object in <{tag.Tag}>, and loads it all the same."
                : null;

        var type = slot.ObjectType?.TypeName ?? slot.ReferenceTypeName;
        if (type is null || type.Equals("GameObjectType", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(resolved.TypeName, type, StringComparison.OrdinalIgnoreCase))
            return null;

        return
            $"'{value}' is a {resolved.TypeName}, not {Article(type)} {type}. The engine accepts any object in <{tag.Tag}>, and loads it all the same.";
    }

    private static string Describe(TupleSlotDefinition slot)
    {
        var type = slot.Enum?.Name ?? slot.Kind?.Kind ?? slot.ObjectType?.TypeName ?? slot.ReferenceTypeName;
        return type is null || type.Equals(slot.Label, StringComparison.OrdinalIgnoreCase)
            ? slot.Label
            : $"{slot.Label} ({type})";
    }

    private static string Article(string word)
    {
        return word.Length > 0 && "AEIOUaeiou".Contains(word[0]) ? "an" : "a";
    }

    private static XmlDiagnosticResult At(ListMapItem item, XmlDiagnosticSeverity severity, string message,
        DiagnosticId id)
    {
        return new XmlDiagnosticResult(severity, message, item.Line, item.Column, item.Text.Length, Id: id);
    }
}