// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Variants;

/// <summary>
///     <see cref="IVariantTagSource" /> backed by the game index. Resolves an object's direct child
///     tags by locating its winning workspace definition (highest project layer, same precedence as
///     <see cref="GameIndex.Resolve(string)" />), loading that single document's text - the open
///     editor buffer when available, the file on disk otherwise - and parsing just that document.
///     Tags read here shadow shipped-game baseline tags.
/// </summary>
/// <remarks>
///     Parses are cached per document and reused while the indexed document's (Version,
///     ContentHash) pair is unchanged, so the resolver can walk multi-level chains without
///     re-parsing, and an edit to one file never re-parses the rest of the workspace.
/// </remarks>
public sealed class WorkspaceVariantTagSource : IVariantTagSource
{
    private const string NameAttribute = "Name";

    // Per-document tag maps keyed by canonical URI. Guarded by _gate.
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly IGameIndexService _indexService;
    private readonly IXmlParseCache _parseCache;

    public WorkspaceVariantTagSource(IXmlParseCache parseCache, IGameIndexService indexService)
    {
        _parseCache = parseCache;
        _indexService = indexService;
    }

    public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
    {
        var index = _indexService.Current;
        if (!index.WorkspaceDefinitions.TryGetValue(objectId, out var defs) || defs.Length == 0)
            return null;

        // Highest project layer wins - the same precedence GameIndex.Resolve applies.
        var winner = defs.Length == 1 ? defs[0] : defs.OrderByDescending(index.LayerRankOf).First();
        if (winner.Origin is not FileOrigin origin)
            return null;

        var map = GetOrBuildDocumentMap(origin.Uri, index);
        return map?.GetValueOrDefault(objectId);
    }

    private Dictionary<string, IReadOnlyList<VariantTag>>? GetOrBuildDocumentMap(string uri, GameIndex index)
    {
        var indexed = index.Documents.GetValueOrDefault(uri);
        var version = indexed?.Version ?? -1;
        var contentHash = indexed?.ContentHash ?? 0;

        lock (_gate)
        {
            if (_cache.TryGetValue(uri, out var entry)
                && entry.Version == version
                && entry.ContentHash == contentHash)
                return entry.TagsById;

            var parsed = _parseCache.GetOrParse(uri);
            if (parsed is null) return null;

            var map = new Dictionary<string, IReadOnlyList<VariantTag>>(StringComparer.OrdinalIgnoreCase);
            IndexDocument(uri, parsed, map);
            _cache[uri] = new CacheEntry(version, contentHash, map);
            return map;
        }
    }

    private static void IndexDocument(string uri, ParsedXmlDocument parsed,
        Dictionary<string, IReadOnlyList<VariantTag>> map)
    {
        foreach (var node in parsed.Html.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var id = GetNameAttribute(node);
            if (id is null) continue;
            // First definition wins; duplicate ids are a workspace error handled elsewhere.
            map.TryAdd(id, CollectChildTags(node, uri, parsed.Text));
        }
    }

    private static string? GetNameAttribute(HtmlNode node)
    {
        var attr = node.Attributes.FirstOrDefault(a =>
            a.Name.Equals(NameAttribute, StringComparison.OrdinalIgnoreCase));
        var value = attr?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static List<VariantTag> CollectChildTags(HtmlNode objectNode, string uri, string text)
    {
        var tags = new List<VariantTag>();
        foreach (var child in objectNode.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var line = XmlUtility.GetLine(child);
            var fragment = SliceOuter(child, text);
            tags.Add(new VariantTag(
                ExtractTagName(fragment) ?? child.Name, // HAP lowercases child.Name; recover original case
                child.InnerText.Trim(),
                fragment,
                line,
                new FileOrigin(uri, line, null)));
        }

        return tags;
    }

    private static string? ExtractTagName(string fragment)
    {
        if (fragment.Length < 2 || fragment[0] != '<') return null;
        var i = 1;
        while (i < fragment.Length && !char.IsWhiteSpace(fragment[i]) && fragment[i] != '>' && fragment[i] != '/')
            i++;
        return i > 1 ? fragment[1..i] : null;
    }

    // Slices the verbatim outer XML from the original text so tag-name casing, whitespace, and
    // multi-line structure are preserved (HtmlAgilityPack lowercases names in OuterHtml).
    private static string SliceOuter(HtmlNode node, string text)
    {
        var start = node.StreamPosition;
        if (start < 0 || start >= text.Length) return node.OuterHtml;

        var end = node.EndNode;
        var searchFrom = end is not null && !ReferenceEquals(end, node) && end.StreamPosition >= start
            ? end.StreamPosition // closing tag start → find its '>'
            : start; // self-closing / leaf without separate end node → find the opening tag's '>'

        var gt = text.IndexOf('>', Math.Min(searchFrom, text.Length - 1));
        var endOffset = gt >= 0 ? gt + 1 : start + node.OuterHtml.Length;
        endOffset = Math.Min(endOffset, text.Length);
        return endOffset > start ? text[start..endOffset] : node.OuterHtml;
    }

    private sealed record CacheEntry(
        int Version,
        long ContentHash,
        Dictionary<string, IReadOnlyList<VariantTag>> TagsById);
}