// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Util;

internal static class HoverUtility
{
    private const string NoneMessage =
        "_No description available. Help the community by [contributing one via a PR](https://github.com/AlamoEngine-Tools/eaw-schema)._";

    public static string Resolve(IReadOnlyDictionary<string, string> descriptions, string locale)
    {
        if (descriptions.TryGetValue(locale, out var text))
            return text;

        if (!string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase) &&
            descriptions.TryGetValue("en", out text))
            return text;

        return NoneMessage;
    }

    public static Hover BuildTypeHover(GameObjectTypeDefinition type, HtmlNode node, string locale)
    {
        var sb = new StringBuilder();
        sb.Append($"### `{type.TypeName}::{node.Name}`");
        var id = XmlUtility.GetXmlObjectId(type, node);
        if (!string.IsNullOrWhiteSpace(id)) sb.Append($" *\"{id}\"*");
        sb.AppendLine();
        sb.Append(Resolve(type.Description, locale));
        AppendNotes(sb, type.Notes, locale);
        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = sb.ToString()
            }),
            Range = MakeRange(XmlUtility.GetLine(node), XmlUtility.GetOpeningTagStartColumn(node), node.Name.Length)
        };
    }

    public static Hover BuildTagHover(XmlTagDefinition tag, HtmlNode node, string locale)
    {
        return BuildTagHover(null, tag, node, locale);
    }

    public static Hover BuildTagHover(GameObjectTypeDefinition? type, XmlTagDefinition tag, HtmlNode node,
        string locale)
    {
        var sb = new StringBuilder();

        if (type is null)
            sb.Append($"### `{tag.Tag}` *`{tag.ValueType}");
        else
            sb.Append($"### `{type.TypeName}::{tag.Tag}` *`{tag.ValueType}");

        if (tag.ReferenceKind != ReferenceKind.None && tag.ReferenceKind != ReferenceKind.Unknown)
        {
            if (tag.ReferenceKind == ReferenceKind.Enum)
                sb.Append($"::{tag.Enum?.Name}");
            else
                sb.Append($"::{tag.ReferenceKind}");
        }

        sb.Append("`*\n");

        // The status line above the description. It repeats what the notes below also say, because
        // this is the line a reader sees before deciding to read on.
        var since = tag.Notes.ValueFor(SchemaNoteKind.Since);
        if (since is not null || tag.Notes.Has(SchemaNoteKind.Deprecated))
        {
            var parts = new List<string>();
            if (since is not null) parts.Add($"Since {since}");
            if (tag.Notes.Has(SchemaNoteKind.Deprecated)) parts.Add("Deprecated");
            sb.AppendLine($"**{string.Join(" - ", parts)}**");
        }

        sb.AppendLine();
        sb.Append(Resolve(tag.Description, locale));

        var hint = ValueTypeHint.Build(tag);
        if (hint is not null)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(hint);
        }

        // Since and Deprecated are already on the status line above the description.
        AppendNotes(sb, tag.Notes, locale, SummarisedOnTheStatusLine);

        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = sb.ToString()
            }),
            Range = MakeRange(XmlUtility.GetLine(node), XmlUtility.GetOpeningTagStartColumn(node), node.Name.Length)
        };
    }

    public static Hover BuildReferenceHover(
        GameObjectTypeDefinition type, string symbolId, GameReference reference, string locale,
        SymbolOrigin? origin = null, string? dependencyLayerName = null)
    {
        var sb = new StringBuilder();
        sb.Append($"### `{type.TypeName}`");
        sb.AppendLine($" *`\"{symbolId}\"`*");
        sb.Append(Resolve(type.Description, locale));
        AppendNotes(sb, type.Notes, locale);
        if (origin is MegArchiveOrigin meg)
            AppendPackedOrigin(sb, meg);
        else if (origin is FileOrigin { IsNavigable: false } shipped)
            AppendShippedOrigin(sb, shipped);
        else if (dependencyLayerName is not null)
            AppendDependencyOrigin(sb, dependencyLayerName);
        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = sb.ToString()
            }),
            Range = MakeRange(reference.Line, reference.Column, reference.Length)
        };
    }

    public static Hover? BuildAssetReferenceHover(
        XmlTagDefinition tag, string rawValue, IAssetFileIndex assetFiles, int line, int col, int length)
    {
        var value = rawValue.Replace('\\', '/').Trim();
        if (string.IsNullOrEmpty(value)) return null;

        var ext = Path.GetExtension(value);
        if (string.IsNullOrEmpty(ext)) return null;

        var matches = FindMatches(assetFiles, value, ext);

        // The engine treats TGA and DDS as one texture (TGA wins when both exist, otherwise it
        // silently falls back to the other format) - resolve the hover the same way.
        if (matches.Count == 0 && tag.ReferenceKind == ReferenceKind.TextureFile)
        {
            var altExt = ext.Equals(".tga", StringComparison.OrdinalIgnoreCase) ? ".dds"
                : ext.Equals(".dds", StringComparison.OrdinalIgnoreCase) ? ".tga"
                : null;
            if (altExt is not null)
                matches = FindMatches(assetFiles, value[..^ext.Length] + altExt, altExt);
        }

        if (matches.Count == 0) return null;

        var kindLabel = tag.ReferenceKind switch
        {
            ReferenceKind.TextureFile => "Texture",
            ReferenceKind.ModelFile => "Model",
            ReferenceKind.AudioFile => "Audio",
            ReferenceKind.MapFile => "Map",
            _ => "Asset"
        };

        var sb = new StringBuilder();
        sb.AppendLine($"### `{value}`");
        sb.AppendLine($"*{kindLabel} file*");

        if (matches.Count == 1)
        {
            var fullPath = matches[0];
            sb.AppendLine();
            sb.Append(assetFiles.IsPackedAsset(fullPath)
                ? $"Packed - `{fullPath}`"
                : $"`{fullPath}`");
        }
        else
        {
            sb.AppendLine();
            foreach (var path in matches)
                sb.AppendLine(assetFiles.IsPackedAsset(path) ? $"- Packed - `{path}`" : $"- `{path}`");
        }

        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = sb.ToString()
            }),
            Range = MakeRange(line, col, length)
        };
    }

    private static List<string> FindMatches(IAssetFileIndex assetFiles, string value, string ext)
    {
        return assetFiles.GetByExtension(ext)
            .Where(p =>
                p.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith("/" + value, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(p).Equals(value, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static void AppendPackedOrigin(StringBuilder sb, MegArchiveOrigin meg)
    {
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.Append(MegArchiveOriginHoverText.Describe(meg));
    }

    // A baseline symbol projected from shipped game data carries a game-relative path (not a file://
    // URI). The exact .meg is not retained, so flag it as packaged base-game data, mirroring how
    // packed binary assets are badged.
    private static void AppendShippedOrigin(StringBuilder sb, FileOrigin shipped)
    {
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.Append($"Packed in the base game - `{shipped.Uri.Replace('\\', '/')}`");
    }

    // A navigable workspace definition that lives in a DEPENDENCY project's layer (rank below the
    // leaf). Without the note it is indistinguishable from a leaf-project definition, and users
    // read the non-editable experience as "broken". Empty name = the layer has no display name.
    private static void AppendDependencyOrigin(StringBuilder sb, string layerName)
    {
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.Append(layerName.Length > 0
            ? $"Defined in dependency **{layerName}**"
            : "Defined in a dependency project");
    }

    // Every note the element carries, in rank order, each labelled by its kind - the reader needs to
    // know whether they are looking at a caveat or at "the engine ignores this". English is the
    // fallback for a note that has not been translated yet: a note in the wrong language still says
    // more than no note at all.
    /// <param name="summarised">
    ///     Kinds a caller has already put in its own status line. A note of such a kind is still
    ///     listed when it has words of its own - the reason a tag was retired is worth reading - but
    ///     a bare one is dropped, because repeating "Deprecated" under a line that just said
    ///     "Deprecated" tells the reader nothing twice.
    /// </param>
    private static void AppendNotes(StringBuilder sb, IReadOnlyList<SchemaNote> notes, string locale,
        IReadOnlySet<SchemaNoteKind>? summarised = null)
    {
        // Ranked here rather than trusted to arrive ranked: the parser ranks what it reads, but a
        // definition built in code has not been through it, and "worst first" is the promise.
        var lines = new List<string>();
        foreach (var note in SchemaNote.Ranked(notes))
        {
            var text = note.Text.GetValueOrDefault(locale) ?? note.Text.GetValueOrDefault("en");
            if (string.IsNullOrWhiteSpace(text) && summarised?.Contains(note.Kind) == true)
                continue;

            var label = $"> **{NoteLabel(note.Kind)}:**";

            // The kind alone is worth saying. Most notes carry nothing else: the schema 2.0.0 sweep
            // turned every `deprecated: true` into a Deprecated note with no words, and dropping
            // those here would silence the majority of what the schema knows.
            lines.Add((text, note.Value) switch
            {
                ({ } t, _) when !string.IsNullOrWhiteSpace(t) => $"{label} *{t}*",
                // A Since note is a version and no prose, so the value reads as its own sentence.
                (_, { } v) => $"{label} {v}",
                _ => label
            });
        }

        if (lines.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        // AppendLine per note, so the block uses the same line ending as everything above it.
        foreach (var line in lines) sb.AppendLine(line);
    }

    /// <summary>The kinds a tag hover puts in its status line, and so need not repeat below it.</summary>
    private static readonly HashSet<SchemaNoteKind> SummarisedOnTheStatusLine =
        [SchemaNoteKind.Since, SchemaNoteKind.Deprecated];

    private static string NoteLabel(SchemaNoteKind kind)
    {
        return kind switch
        {
            SchemaNoteKind.BuggedInEngine => "Does not work",
            SchemaNoteKind.Deprecated => "Deprecated",
            SchemaNoteKind.Untested => "Untested",
            SchemaNoteKind.Since => "Since",
            _ => "Note"
        };
    }

    private static Range MakeRange(int line, int colStart, int length)
    {
        return new Range(new Position(line, colStart), new Position(line, colStart + length));
    }
}