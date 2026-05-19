// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.StoryScripting;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class StoryParserDiagnosticCollector(ISchemaProvider schema)
{
    private const int MaxEventParamSlots = 7;
    private const int MaxRewardParamSlots = 14;

    public IReadOnlyList<Diagnostic> Collect(string xmlContent, GameIndex gameIndex)
    {
        var diagnostics = new List<Diagnostic>();
        var doc = new HtmlDocument();
        doc.LoadHtml(xmlContent);

        foreach (var eventNode in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element &&
                                 string.Equals(n.Name, "Event", StringComparison.OrdinalIgnoreCase)))
        {
            CollectForEvent(eventNode, gameIndex, diagnostics);
        }

        return diagnostics;
    }

    private void CollectForEvent(HtmlNode eventNode, GameIndex gameIndex, List<Diagnostic> diagnostics)
    {
        var eventType = GetChildText(eventNode, "Event_Type");
        if (eventType is not null)
        {
            var eventDef = StoryScriptingIndex.GetEvent(eventType);
            if (eventDef is not null)
                CheckParams(eventNode, "Event_Param", MaxEventParamSlots,
                    eventDef.Params, eventType, gameIndex, diagnostics);
        }

        var rewardType = GetChildText(eventNode, "Reward_Type");
        if (rewardType is not null)
        {
            var rewardDef = StoryScriptingIndex.GetReward(rewardType);
            if (rewardDef is not null)
                CheckParams(eventNode, "Reward_Param", MaxRewardParamSlots,
                    rewardDef.Params, rewardType, gameIndex, diagnostics);
        }
    }

    private void CheckParams(
        HtmlNode eventNode,
        string paramPrefix,
        int maxSlots,
        IReadOnlyList<StoryParamDefinition> paramDefs,
        string typeName,
        GameIndex gameIndex,
        List<Diagnostic> diagnostics)
    {
        for (var n = 1; n <= maxSlots; n++)
        {
            var tagName = $"{paramPrefix}{n}";
            var child = FindChild(eventNode, tagName);
            if (child is null) continue;
            var value = child.InnerText.Trim();
            if (value.Length == 0) continue;

            if (n > paramDefs.Count)
            {
                diagnostics.Add(Warn(child, $"'{tagName}' is not used by {typeName}."));
            }
            else
            {
                var msg = ValidateParamValue(paramDefs[n - 1], value, gameIndex);
                if (msg is not null)
                    diagnostics.Add(Warn(child, msg));
            }
        }

        for (var n = 1; n <= paramDefs.Count; n++)
        {
            if (!paramDefs[n - 1].Required) continue;
            var tagName = $"{paramPrefix}{n}";
            var child = FindChild(eventNode, tagName);
            if (child is not null && child.InnerText.Trim().Length > 0) continue;

            var def = paramDefs[n - 1];
            var msg = def.Description is not null
                ? $"{typeName} requires {tagName} ({def.Description})."
                : $"{typeName} requires {tagName}.";
            diagnostics.Add(Warn(eventNode, msg));
        }
    }

    private string? ValidateParamValue(StoryParamDefinition def, string value, GameIndex gameIndex)
    {
        return def.Kind switch
        {
            StoryParamKind.Integer =>
                int.TryParse(value, out _) ? null : $"'{value}' is not a valid integer.",
            StoryParamKind.PositiveInteger =>
                int.TryParse(value, out var pi) && pi > 0 ? null : $"'{value}' is not a valid positive integer.",
            StoryParamKind.FloatSeconds =>
                float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    ? null : $"'{value}' is not a valid number.",
            StoryParamKind.BooleanInt =>
                IsBooleanInt(value) ? null : $"'{value}' is not a valid boolean value. Use 0, 1, true, or false.",
            StoryParamKind.EraNumber =>
                int.TryParse(value, out var e) && e >= 1 && e <= 5 ? null
                    : $"'{value}' is not a valid era number (expected 1–5).",
            StoryParamKind.TechLevel =>
                int.TryParse(value, out var t) && t >= 1 && t <= 5 ? null
                    : $"'{value}' is not a valid tech level (expected 1–5).",
            StoryParamKind.Enum =>
                ValidateEnum(def.EnumName, value),
            StoryParamKind.EnumList =>
                ValidateEnumList(def.EnumName, value),
            _ when def.ReferenceType is not null =>
                gameIndex.Resolve(value) is not null ? null
                    : $"'{value}' is not a recognized {def.ReferenceType}.",
            _ => null
        };
    }

    private static bool IsBooleanInt(string value)
        => value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("false", StringComparison.OrdinalIgnoreCase);

    private string? ValidateEnum(string? enumName, string value)
    {
        if (enumName is null) return null;
        var enumDef = schema.GetEnum(enumName);
        if (enumDef is null) return null;
        return enumDef.Values.Any(v => string.Equals(v.Name, value, StringComparison.OrdinalIgnoreCase))
            ? null
            : $"'{value}' is not a valid {enumName} value.";
    }

    private string? ValidateEnumList(string? enumName, string value)
    {
        if (enumName is null) return null;
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var msg = ValidateEnum(enumName, token);
            if (msg is not null) return msg;
        }
        return null;
    }

    private static HtmlNode? FindChild(HtmlNode parent, string tagName)
        => parent.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            string.Equals(n.Name, tagName, StringComparison.OrdinalIgnoreCase));

    private static string? GetChildText(HtmlNode parent, string tagName)
        => FindChild(parent, tagName)?.InnerText.Trim();

    private static Diagnostic Warn(HtmlNode node, string message) => new()
    {
        Severity = DiagnosticSeverity.Warning,
        Message = message,
        Range = new Range(
            new Position(Math.Max(0, node.Line - 1), 0),
            new Position(Math.Max(0, node.Line - 1), int.MaxValue)),
        Source = "pg-swg-lsp"
    };
}
