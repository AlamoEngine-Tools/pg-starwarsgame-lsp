// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     The tail of <c>GameConstants.xml</c>'s <c>Damage_Types</c> list, checked against the entries
///     the engine reads at fixed positions from the list's end. A list too short to hold the tail,
///     or an entry out of its place, crashes the game at runtime.
/// </summary>
public sealed class DamageTypeTailFactProducer : IXmlFactProducer
{
    /// <summary>The 20 damage types the engine hardcodes, in the order it expects them at the end of the list.</summary>
    public static readonly string[] RequiredTail =
    [
        "Damage_Normal", "Damage_Force_Whirlwind", "Damage_Force_Telekinesis", "Damage_Force_Lightning",
        "Damage_Force_Corruption", "Damage_Hard_Point_Self_Destruct", "Damage_Fire", "Damage_Cable_Attack",
        "Damage_Explosion", "Damage_Asteroid", "Damage_Cable_Attack_Deployed", "Damage_Normal_Deployed",
        "Damage_Vehicle_Thief", "Damage_Crush", "Damage_Eat", "Damage_Redirected", "Damage_Wampa",
        "Damage_Infection", "Damage_Remote_Bomb", "Damage_Drain_Life"
    ];

    private static readonly char[] Separators = [' ', '\t', '\r', '\n', ','];

    public string Name => "damageTypeOrder";

    /// <summary>Every document: which one declares the list is only known once it is read.</summary>
    public bool AppliesTo(string documentUri)
    {
        return true;
    }

    public IReadOnlyList<XmlFact> Produce(string documentUri, ParsedXmlDocument document, GameIndex index)
    {
        // Cheap pre-check before walking the tree - almost no documents declare this element.
        if (!document.Text.Contains("Damage_Types", StringComparison.OrdinalIgnoreCase))
            return [];

        var element = document.Html.DocumentNode.Descendants().FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element && n.Name.Equals("Damage_Types", StringComparison.OrdinalIgnoreCase));
        if (element is null)
            return [];

        var tokens = new List<(string Token, int AbsPos)>();
        foreach (var node in element.ChildNodes)
        {
            if (node.NodeType != HtmlNodeType.Text) continue;
            foreach (var (token, offset) in Split(node.InnerText))
                tokens.Add((token, node.StreamPosition + offset));
        }

        var lineIndex = document.LineIndex;
        if (tokens.Count < RequiredTail.Length)
        {
            var (line, column) = tokens.Count > 0
                ? lineIndex.GetPosition(tokens[^1].AbsPos)
                : lineIndex.GetPosition(element.InnerStartIndex);
            var length = tokens.Count > 0 ? tokens[^1].Token.Length : 0;
            return
                [new DamageTypesIncompleteFact(documentUri, line, column, length, tokens.Count, RequiredTail.Length)];
        }

        var tail = tokens.GetRange(tokens.Count - RequiredTail.Length, RequiredTail.Length);
        var facts = new List<XmlFact>();
        for (var i = 0; i < RequiredTail.Length; i++)
        {
            if (string.Equals(tail[i].Token, RequiredTail[i], StringComparison.Ordinal))
                continue;

            var (line, column) = lineIndex.GetPosition(tail[i].AbsPos);
            facts.Add(new DamageTypeOutOfOrderFact(documentUri, line, column, tail[i].Token.Length,
                tail[i].Token, RequiredTail[i], RequiredTail.Length));
        }

        return facts;
    }

    private static IEnumerable<(string Token, int Offset)> Split(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && Array.IndexOf(Separators, text[i]) >= 0) i++;
            if (i >= text.Length) break;
            var start = i;
            while (i < text.Length && Array.IndexOf(Separators, text[i]) < 0) i++;
            yield return (text[start..i], start);
        }
    }
}