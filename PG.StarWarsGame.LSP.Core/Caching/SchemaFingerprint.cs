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
            sb.Append("tag:").Append(tag.Tag)
                .Append('|').Append(tag.ValueType)
                .Append('|').Append(tag.ReferenceKind)
                .Append('|').Append(tag.ObjectType?.TypeName)
                .Append('|').Append(tag.Enum?.Name)
                .Append('|').Append(tag.SemanticType)
                .Append('|').Append(tag.ValidationOverride?.ValidationId)
                .Append('\n');

        foreach (var type in schema.AllObjectTypes.OrderBy(t => t.TypeName, StringComparer.Ordinal))
            sb.Append("type:").Append(type.TypeName)
                .Append('|').Append(type.NameTag)
                .Append('\n');

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
