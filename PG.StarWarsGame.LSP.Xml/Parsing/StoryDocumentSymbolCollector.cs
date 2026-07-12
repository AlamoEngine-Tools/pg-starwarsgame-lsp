// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Parsing;

/// <summary>
///     Emits story symbols and references for a <c>StoryParser</c> document, schema-driven by
///     the <c>referenceType</c> annotations on the event/reward enums: <c>&lt;Event Name&gt;</c>
///     defines a <c>StoryEvent</c>, <c>SET_FLAG</c> param 1 defines a <c>StoryFlag</c>; prereq
///     tokens and reference-typed params emit column-accurate <see cref="GameReference" />s
///     (rename needs exact spans). Story references are exempt from index-wide existence and
///     duplicate validation — that stays campaign-scoped in the story graph diagnostics.
/// </summary>
internal static class StoryDocumentSymbolCollector
{
    public static void Collect(ParsedXmlDocument parsed, string documentUri, ISchemaProvider schema,
        List<GameSymbol> symbols, List<GameReference> references)
    {
        var eventParamTypes = StoryReferenceTypes.BuildParamMap(schema.GetEnum("StoryEventType"));
        var rewardParamTypes = StoryReferenceTypes.BuildParamMap(schema.GetEnum("StoryRewardType"));

        foreach (var eventNode in parsed.Html.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element &&
                                 n.Name.Equals("Event", StringComparison.OrdinalIgnoreCase)))
        {
            CollectEventNameSymbol(parsed, documentUri, eventNode, symbols);

            var eventType = FindChild(eventNode, "Event_Type")?.InnerText.Trim().ToUpperInvariant();
            var rewardType = FindChild(eventNode, "Reward_Type")?.InnerText.Trim().ToUpperInvariant();

            foreach (var child in eventNode.ChildNodes.Where(c => c.NodeType == HtmlNodeType.Element))
                if (child.Name.Equals("Prereq", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var (token, line, column) in Tokens(parsed, child))
                        references.Add(new GameReference(token, GameSymbolKind.XmlObject,
                            StoryReferenceTypes.EventSymbol, documentUri, line, column, token.Length));
                }
                else if (TryParamPosition(child.Name, "Event_Param", out var eventPos) && eventType is not null)
                {
                    CollectParam(parsed, documentUri, child, eventType, eventPos, eventParamTypes,
                        isReward: false, symbols, references);
                }
                else if (TryParamPosition(child.Name, "Reward_Param", out var rewardPos) && rewardType is not null)
                {
                    CollectParam(parsed, documentUri, child, rewardType, rewardPos, rewardParamTypes,
                        isReward: true, symbols, references);
                }
        }
    }

    private static void CollectEventNameSymbol(ParsedXmlDocument parsed, string documentUri,
        HtmlNode eventNode, List<GameSymbol> symbols)
    {
        var nameAttribute = eventNode.Attributes["Name"];
        var name = nameAttribute?.Value.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var (line, column) = parsed.LineIndex.GetPosition(nameAttribute!.ValueStartIndex);
        symbols.Add(new GameSymbol(name, GameSymbolKind.XmlObject, StoryReferenceTypes.EventSymbol,
            new FileOrigin(documentUri, line, column), null));
    }

    private static void CollectParam(ParsedXmlDocument parsed, string documentUri, HtmlNode paramNode,
        string typeUpper, int position, Dictionary<(string, int), string> paramTypes, bool isReward,
        List<GameSymbol> symbols, List<GameReference> references)
    {
        if (!paramTypes.TryGetValue((typeUpper, position), out var refType)) return;

        var symbolType = refType switch
        {
            StoryReferenceTypes.EventName => StoryReferenceTypes.EventSymbol,
            StoryReferenceTypes.Flag => StoryReferenceTypes.FlagSymbol,
            StoryReferenceTypes.Notification => StoryReferenceTypes.NotificationSymbol,
            _ => null // plot files and branches are not symbols
        };
        if (symbolType is null) return;

        // SET_FLAG creates the flag — its param is the definition; every other flag param reads.
        var defines = isReward && typeUpper == "SET_FLAG" && position == 0;

        foreach (var (token, line, column) in Tokens(parsed, paramNode))
            if (defines)
                symbols.Add(new GameSymbol(token, GameSymbolKind.XmlObject, symbolType,
                    new FileOrigin(documentUri, line, column), null));
            else
                references.Add(new GameReference(token, GameSymbolKind.XmlObject, symbolType,
                    documentUri, line, column, token.Length));
    }

    // "Event_Param3" → position 2; "Reward_Param_List" fails the numeric parse by design.
    private static bool TryParamPosition(string tagName, string prefix, out int position)
    {
        position = -1;
        if (!tagName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (!int.TryParse(tagName.AsSpan(prefix.Length), out var slot) || slot < 1) return false;
        position = slot - 1;
        return true;
    }

    private static IEnumerable<(string Token, int Line, int Column)> Tokens(
        ParsedXmlDocument parsed, HtmlNode element)
    {
        if (element.InnerLength <= 0) yield break;
        var innerStart = element.InnerStartIndex;
        var raw = parsed.Text.Substring(innerStart, element.InnerLength);

        var i = 0;
        while (i < raw.Length)
        {
            if (char.IsWhiteSpace(raw[i]) || raw[i] == ',')
            {
                i++;
                continue;
            }

            var start = i;
            while (i < raw.Length && !char.IsWhiteSpace(raw[i]) && raw[i] != ',') i++;
            var (line, column) = parsed.LineIndex.GetPosition(innerStart + start);
            yield return (raw[start..i], line, column);
        }
    }

    private static HtmlNode? FindChild(HtmlNode parent, string tagName)
    {
        return parent.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            string.Equals(n.Name, tagName, StringComparison.OrdinalIgnoreCase));
    }
}
