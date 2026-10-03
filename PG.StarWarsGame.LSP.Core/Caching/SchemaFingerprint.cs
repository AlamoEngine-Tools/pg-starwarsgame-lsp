// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Caching;

/// <summary>
///     Deterministic fingerprint over every parse-relevant part of the loaded schema. The schema
///     is an INPUT to document parsing (which tags produce references, which files carry types,
///     which enums exist), so a project index snapshot written under one schema must not be
///     replayed under another — per-file content hashes can never catch that. Stored in
///     <see cref="ProjectIndexSnapshot.SchemaFingerprint" /> and compared on load; a mismatch
///     discards the snapshot. Order-independent: entries are sorted before hashing.
/// </summary>
public static class SchemaFingerprint
{
    public static string Compute(ISchemaProvider schema)
    {
        var sb = new StringBuilder();

        foreach (var tag in schema.AllTags.OrderBy(t => t.Tag, StringComparer.Ordinal))
        {
            sb.Append("tag:").Append(tag.Tag)
                .Append('|').Append(tag.ValueType)
                .Append('|').Append(tag.ReferenceKind)
                .Append('|').Append(tag.ObjectType?.TypeName)
                .Append('|').Append(tag.Enum?.Name)
                .Append('|').Append(tag.SemanticType)
                .Append('|').Append(tag.ValidationOverride?.ValidationId);

            // Slots decide which items of a tuple value the parser records as references, so a
            // slot-only change has to discard the snapshots too. In order: position is meaning.
            foreach (var slot in tag.Slots)
                sb.Append("|slot:").Append(slot.ReferenceKind)
                    .Append(',').Append(slot.ReferenceTypeName)
                    .Append(',').Append(slot.Enum?.Name);

            sb.Append('\n');
        }

        foreach (var type in schema.AllObjectTypes.OrderBy(t => t.TypeName, StringComparer.Ordinal))
            sb.Append("type:").Append(type.TypeName)
                .Append('|').Append(type.NameTag)
                .Append('\n');

        // Kinds drive what the PARSER emits, not just how a value is judged: the flags a symbol
        // carries are exactly the ones some kind asks about. A snapshot written under a kinds file
        // that named no flag would otherwise replay flagless symbols forever.
        foreach (var kind in schema.AllKinds.OrderBy(k => k.Kind, StringComparer.Ordinal))
        {
            sb.Append("kind:").Append(kind.Kind);
            foreach (var behavior in kind.Behaviors.OrderBy(b => b, StringComparer.Ordinal))
                sb.Append("|b:").Append(behavior);
            foreach (var flag in kind.Flags.OrderBy(f => f, StringComparer.Ordinal))
                sb.Append("|f:").Append(flag);
            foreach (var member in kind.MemberOf.OrderBy(m => m, StringComparer.Ordinal))
                sb.Append("|m:").Append(member);
            sb.Append('\n');
        }

        foreach (var e in schema.AllEnums.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            sb.Append("enum:").Append(e.Name)
                .Append('|').Append(e.Kind)
                .Append('|').Append(e.SourceFile);
            foreach (var v in e.Values.OrderBy(v => v.Name, StringComparer.Ordinal))
                sb.Append('|').Append(v.Name);
            sb.Append('\n');
        }

        foreach (var m in schema.AllMetafiles.OrderBy(m => m.Path, StringComparer.Ordinal))
        {
            sb.Append("meta:").Append(m.Path)
                .Append('|').Append(m.MetafileType);
            foreach (var t in m.Types.OrderBy(t => t, StringComparer.Ordinal))
                sb.Append('|').Append(t);
            sb.Append('\n');
        }

        // A scanned directory decides whether the files under it are exempt from the
        // unregistered-file check, so it changes what a persisted index means. Leaving it out would
        // let an index built under the old list be reused under the new one.
        foreach (var d in schema.AllScannedDirectories.OrderBy(d => d.Path, StringComparer.Ordinal))
        {
            sb.Append("scan:").Append(d.Path);
            foreach (var t in d.Types.OrderBy(t => t, StringComparer.Ordinal))
                sb.Append('|').Append(t);
            sb.Append('\n');
        }

        foreach (var set in schema.AllHardcodedSets.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            sb.Append("set:").Append(set.Name);
            foreach (var v in set.Values.OrderBy(v => v.Name, StringComparer.Ordinal))
                sb.Append('|').Append(v.Name);
            sb.Append('\n');
        }

        return ContentHasher.Hash(sb.ToString()).ToString("x16");
    }
}