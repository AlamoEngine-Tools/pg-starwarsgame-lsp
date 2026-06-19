// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Variants;

/// <summary>
///     <see cref="IVariantTagSource" /> backed by the open workspace documents. Resolves an object's direct
///     child tags by locating the element whose <c>Name</c> attribute matches the id (every named GameObject
///     type uses <c>Name</c> as its name tag). Tags read here shadow shipped-game baseline tags.
/// </summary>
/// <remarks>
///     Results are cached and rebuilt only when the set of open documents or their versions changes, so the
///     resolver can walk multi-level chains without re-parsing every document on each lookup.
/// </remarks>
public sealed class WorkspaceVariantTagSource : IVariantTagSource
{
    private const string NameAttribute = "Name";
    private readonly object _gate = new();

    private readonly IGameWorkspaceHost _host;

    private Dictionary<string, IReadOnlyList<VariantTag>> _byId = new(StringComparer.OrdinalIgnoreCase);
    private string? _signature;

    public WorkspaceVariantTagSource(IGameWorkspaceHost host)
    {
        _host = host;
    }

    public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
    {
        lock (_gate)
        {
            EnsureCurrent();
            return _byId.GetValueOrDefault(objectId);
        }
    }

    private void EnsureCurrent()
    {
        var docs = _host.All.ToList();
        var signature = string.Join("", docs.Select(d => $"{d.Uri}{d.Version}"));
        if (signature == _signature) return;

        var map = new Dictionary<string, IReadOnlyList<VariantTag>>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in docs)
            IndexDocument(doc.Uri, doc.Text, map);

        _byId = map;
        _signature = signature;
    }

    private static void IndexDocument(string uri, string text, Dictionary<string, IReadOnlyList<VariantTag>> map)
    {
        var hapDoc = XmlUtility.CreateHtmlDocument(text);
        foreach (var node in hapDoc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var id = GetNameAttribute(node);
            if (id is null) continue;
            // First definition wins; duplicate ids are a workspace error handled elsewhere.
            map.TryAdd(id, CollectChildTags(node, uri, text));
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
}