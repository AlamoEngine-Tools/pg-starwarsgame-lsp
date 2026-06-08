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

        if (tag.AvailableSince is not null || tag.Deprecated)
        {
            var parts = new List<string>();
            if (tag.AvailableSince is not null) parts.Add($"Since {tag.AvailableSince}");
            if (tag.Deprecated) parts.Add("Deprecated");
            sb.AppendLine($"**{string.Join(" · ", parts)}**");
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

        AppendNotes(sb, tag.Notes, locale);

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
        SymbolOrigin? origin = null)
    {
        var sb = new StringBuilder();
        sb.Append($"### `{type.TypeName}`");
        sb.AppendLine($" *`\"{symbolId}\"`*");
        sb.Append(Resolve(type.Description, locale));
        AppendNotes(sb, type.Notes, locale);
        if (origin is MegArchiveOrigin meg)
            AppendPackedOrigin(sb, meg);
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

        var matches = assetFiles.GetByExtension(ext)
            .Where(p =>
                p.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith("/" + value, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(p).Equals(value, StringComparison.OrdinalIgnoreCase))
            .ToList();

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
                ? $"📦 `{fullPath}`"
                : $"`{fullPath}`");
        }
        else
        {
            sb.AppendLine();
            foreach (var path in matches)
                sb.AppendLine(assetFiles.IsPackedAsset(path) ? $"- 📦 `{path}`" : $"- `{path}`");
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

    private static void AppendPackedOrigin(StringBuilder sb, MegArchiveOrigin meg)
    {
        var archiveName = Path.GetFileName(meg.ArchivePath);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.Append($"📦 Packed in `{archiveName}` → `{meg.InternalPath}`");
    }

    private static void AppendNotes(StringBuilder sb, IReadOnlyDictionary<string, string> notes, string locale)
    {
        if (!notes.TryGetValue(locale, out var note) && !notes.TryGetValue("en", out note))
            return;
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.Append($"> **Note:** *{note}*");
    }

    private static Range MakeRange(int line, int colStart, int length)
    {
        return new Range(new Position(line, colStart), new Position(line, colStart + length));
    }
}