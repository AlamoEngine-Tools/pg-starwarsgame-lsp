// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json.Nodes;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Persistence;

namespace PG.StarWarsGame.LSP.Server.Suppression;

/// <summary>
///     What <c>.aetswg/suppressions.json</c> is, as a versioned document.
///     <para>
///         The version, the shape pin and the migration chain live together on purpose: a change to
///         any one of them without the others is the mistake this is here to catch.
///     </para>
/// </summary>
public static class SuppressionsDocument
{
    public const string TypeName = "aetswg.Suppressions";

    /// <summary>
    ///     Bump this when <see cref="Payload" /> changes shape, add a migration from the version
    ///     before it, and re-pin <see cref="Shape" />. <c>Document_StillHasThePinnedShape</c> fails
    ///     until all three are done.
    /// </summary>
    public static readonly TypeVersion Version = TypeVersion.Of("aetswg", 2);

    /// <summary>The shape <see cref="Version" /> describes. See <see cref="DocumentSignature" />.</summary>
    public static readonly DocumentShapePin Shape = new(
        TypeName, Version, "d65d992874bef193fddb0a81ee7d192762cc2ed736fd31a5561f28cccba863f0");

    public static readonly IReadOnlyList<IDocumentMigration> Migrations =
        [new AdoptBareArray(), new AddDefaultOffEntries()];

    /// <summary>
    ///     Diagnostics a project starts with switched off. They are entries in the project's own file,
    ///     not a hidden list, so the author sees them and turns one on by deleting its line.
    /// </summary>
    public static IReadOnlyList<SuppressionEntry> DefaultOff =>
    [
        new()
        {
            Id = DiagnosticIds.XmlCommentInsideValue.ToString(),
            Reason = "Off by default: comments inside values are common practice; delete this entry to report them"
        }
    ];

    /// <summary>The document a project has before it writes one: the default-off entries.</summary>
    public static Payload Default()
    {
        return new Payload { Entries = [.. DefaultOff] };
    }

    /// <summary>The document body: the entries, under a name, because an array cannot hold an envelope.</summary>
    public sealed record Payload
    {
        public List<SuppressionEntry> Entries { get; init; } = [];
    }

    /// <summary>
    ///     Version 2 introduced default-off diagnostics. A file written before then gains each entry
    ///     once, here; a version-2 file without it is a project that turned it on, and no later read
    ///     adds it back.
    /// </summary>
    private sealed class AddDefaultOffEntries : IDocumentMigration
    {
        public string TypeName => SuppressionsDocument.TypeName;
        public TypeVersion From => TypeVersion.Of("aetswg", 1);
        public TypeVersion To => Version;

        // Nothing for the user to do: the new entry is visible in the file and explains itself.
        public string? UserNotice => null;

        public JsonNode Migrate(JsonNode document)
        {
            if (document is not JsonObject root) return document;
            if (root["entries"] is not JsonArray entries)
                root["entries"] = entries = [];
            var present = entries.Select(e => (string?)e?["id"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in DefaultOff)
                if (!present.Contains(entry.Id))
                    entries.Add(new JsonObject { ["id"] = entry.Id, ["reason"] = entry.Reason });
            return root;
        }
    }

    /// <summary>
    ///     Version zero was the bare array this file shipped as. Wrapping it is the whole migration
    ///     - no entry changes - and it is what gives the file somewhere to say what it is.
    /// </summary>
    private sealed class AdoptBareArray : IDocumentMigration
    {
        public string TypeName => SuppressionsDocument.TypeName;
        public TypeVersion From => TypeVersion.Zero("aetswg");
        public TypeVersion To => Version;

        // Nothing for the user to do: the file keeps every suppression it had, at the same ids.
        public string? UserNotice => null;

        public JsonNode Migrate(JsonNode document)
        {
            return document is JsonArray entries
                ? new JsonObject { ["entries"] = entries.DeepClone() }
                : document;
        }
    }
}