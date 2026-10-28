// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     Reads the reference-group memberships out of the game's files. Parsed through
///     <see cref="XmlUtility" /> like every other game XML (#308): a file a strict parser rejects is
///     read the way the game reads it.
/// </summary>
public static class GroupMembershipExtractor
{
    /// <summary>
    ///     Extracts group memberships for all entries whose XML files can be read.
    /// </summary>
    /// <param name="entries">
    ///     Named game objects paired with the archive-root-relative path to the XML file that defines them.
    ///     Entries sharing a file path are deduplicated - each file is read at most once.
    /// </param>
    /// <param name="readFile">
    ///     Callback that returns the XML text for a given file path, or <see langword="null" /> if
    ///     the file is unavailable.
    /// </param>
    /// <param name="schema">Schema used to identify <see cref="TagSemanticType.ReferenceGroup" /> tags.</param>
    public static ImmutableDictionary<string, ImmutableArray<GroupMembership>> Extract(
        IEnumerable<(string name, string xmlFilePath)> entries,
        Func<string, string?> readFile,
        ISchemaProvider schema)
    {
        var groupTags = schema.AllTags
            .Where(t => t.SemanticType == TagSemanticType.ReferenceGroup)
            .ToList();

        if (groupTags.Count == 0)
            return ImmutableDictionary.Create<string, ImmutableArray<GroupMembership>>(
                StringComparer.OrdinalIgnoreCase);

        var groupTagsByName = groupTags.ToDictionary(
            t => t.Tag, t => t, StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, List<GroupMembership>>(StringComparer.OrdinalIgnoreCase);
        var readFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, filePath) in entries)
        {
            if (!readFiles.Add(filePath)) continue;

            var content = readFile(filePath);
            if (string.IsNullOrEmpty(content)) continue;

            var doc = XmlUtility.CreateHtmlDocument(content);
            foreach (var element in doc.DocumentNode.Descendants().Where(n => n.NodeType == HtmlNodeType.Element))
            foreach (var child in element.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
            {
                if (!groupTagsByName.TryGetValue(child.Name, out var tagDef)) continue;

                var groupKey = child.InnerText.Trim();
                if (string.IsNullOrEmpty(groupKey)) continue;

                var line = Math.Max(0, XmlUtility.GetLine(element));
                var memberTypeName = tagDef.ObjectType?.TypeName;
                var membership = new GroupMembership(groupKey, memberTypeName,
                    new FileOrigin(filePath, line, null));

                if (!result.TryGetValue(groupKey, out var list))
                    result[groupKey] = list = new List<GroupMembership>();
                list.Add(membership);
            }
        }

        return result.ToImmutableDictionary(
            kv => kv.Key,
            kv => kv.Value.ToImmutableArray(),
            StringComparer.OrdinalIgnoreCase);
    }
}