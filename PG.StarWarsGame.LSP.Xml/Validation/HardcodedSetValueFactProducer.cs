// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     Every token of a hardcoded-set tag that the set does not accept as written: unknown to the set,
///     ruled out by the tag's value groups, or known but deprecated. The engine reads these values by
///     name from a table compiled into it, so a misspelling is not a value it ignores but one it
///     cannot resolve.
/// </summary>
public sealed class HardcodedSetValueFactProducer(
    ISchemaProvider schema,
    IFileTypeRegistry fileTypeRegistry,
    IFileHelper fileHelper) : IXmlFactProducer
{
    private static readonly char[] Separators = [',', ' ', '\t', '\r', '\n'];

    public string Name => "hardcodedRefs";

    /// <summary>
    ///     A document whose registered types declare no hardcoded-set tag has nothing to walk. One
    ///     with no registered type is walked in full, since nothing says what it holds.
    /// </summary>
    public bool AppliesTo(string documentUri)
    {
        if (schema.AllHardcodedSets.Count == 0) return false;

        var fileTypes = fileTypeRegistry.GetTypesForFile(fileHelper.NormalizeUri(documentUri));
        return fileTypes.IsDefaultOrEmpty
               || fileTypes.Any(t =>
                   schema.GetTagsForType(t).Any(tag => tag.ReferenceKind == ReferenceKind.HardcodedSet));
    }

    public IReadOnlyList<XmlFact> Produce(string documentUri, ParsedXmlDocument document, GameIndex index)
    {
        var facts = new List<XmlFact>();
        Walk(document.Html.DocumentNode, documentUri, document.LineIndex, facts);
        return facts;
    }

    private void Walk(HtmlNode node, string documentUri, LineOffsetIndex lineIndex, List<XmlFact> facts)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Element) continue;

            var tagDef = schema.GetTag(child.Name);
            if (tagDef is { ReferenceKind: ReferenceKind.HardcodedSet, HardcodedSet: not null })
                Collect(child, documentUri, tagDef, tagDef.HardcodedSet, lineIndex, facts);

            Walk(child, documentUri, lineIndex, facts);
        }
    }

    private static void Collect(
        HtmlNode element, string documentUri, XmlTagDefinition tagDef, HardcodedReferenceSet set,
        LineOffsetIndex lineIndex, List<XmlFact> facts)
    {
        var groups = tagDef.ValueGroups;
        var applicableValues = groups.Count == 0
            ? set.Values
            : set.Values.Where(v => v.Groups.Count == 0 ||
                                    v.Groups.Any(g => groups.Contains(g, StringComparer.OrdinalIgnoreCase)));
        var deprecatedByName = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in applicableValues)
            deprecatedByName[value.Name] = value.Notes.Has(SchemaNoteKind.Deprecated);

        var innerText = element.InnerText;
        var i = 0;
        while (i < innerText.Length)
        {
            while (i < innerText.Length && Array.IndexOf(Separators, innerText[i]) >= 0) i++;
            if (i >= innerText.Length) break;

            var tokenStart = i;
            while (i < innerText.Length && Array.IndexOf(Separators, innerText[i]) < 0) i++;

            var token = innerText[tokenStart..i];
            var known = deprecatedByName.TryGetValue(token, out var deprecated);
            if (known && !deprecated) continue;

            var (line, column, length) =
                XmlUtility.GetInnerOffsetValuePosition(element, tokenStart, token.Length, lineIndex);
            facts.Add(new HardcodedSetValueFact(documentUri, line, column, length, token, set.Name, known));
        }
    }
}