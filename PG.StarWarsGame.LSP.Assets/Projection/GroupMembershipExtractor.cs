// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     Extracts <see cref="GroupMembership" /> records from shipped-game XML files and populates
///     <see cref="BaselineIndex.GroupMemberships" />.
/// </summary>
/// <remarks>
///     Only tags declared with <see cref="TagSemanticType.ReferenceGroup" /> in the schema are
///     extracted. Today that is only <c>Overlap_Test</c> in <c>SFXEvent</c>, but any new
///     ReferenceGroup tags added to the schema are handled automatically.
/// </remarks>
public static class GroupMembershipExtractor
{
    /// <summary>
    ///     Extracts group memberships for all entries whose XML files can be read.
    /// </summary>
    /// <param name="entries">
    ///     Named game objects paired with the archive-root-relative path to the XML file that defines them.
    ///     Entries sharing a file path are deduplicated — each file is read at most once.
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

            XDocument doc;
            try
            {
                doc = XDocument.Load(new StringReader(content), LoadOptions.SetLineInfo);
            }
            catch
            {
                continue;
            }

            foreach (var element in doc.Descendants())
            foreach (var child in element.Elements())
            {
                if (!groupTagsByName.TryGetValue(child.Name.LocalName, out var tagDef)) continue;

                var groupKey = child.Value.Trim();
                if (string.IsNullOrEmpty(groupKey)) continue;

                var line = element is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber - 1 : 0;
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