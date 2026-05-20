// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class StoryParserDiagnosticCollector(ISchemaProvider schema)
{
    private const int MaxEventParamSlots = 7;
    private const int MaxRewardParamSlots = 14;
    private const string Locale = "en";

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
        var eventTypeNode = FindChild(eventNode, "Event_Type");
        var eventType = eventTypeNode?.InnerText.Trim();
        if (eventType is not null)
        {
            var eventDef = GetEventDef(eventType);
            if (eventDef is not null)
            {
                if (eventDef.Deprecated)
                    diagnostics.Add(Warn(eventTypeNode!, $"'{eventType}' is deprecated."));
                if (eventDef.Notes.TryGetValue(Locale, out var en))
                    diagnostics.Add(Hint(eventTypeNode!, en));

                CheckParams(eventNode, "Event_Param", MaxEventParamSlots,
                    eventDef.Params, eventType, gameIndex, diagnostics);
            }
        }

        var rewardTypeNode = FindChild(eventNode, "Reward_Type");
        var rewardType = rewardTypeNode?.InnerText.Trim();
        if (rewardType is not null)
        {
            var rewardDef = GetRewardDef(rewardType);
            if (rewardDef is not null)
            {
                if (rewardDef.Deprecated)
                    diagnostics.Add(Warn(rewardTypeNode!, $"'{rewardType}' is deprecated."));
                if (rewardDef.Notes.TryGetValue(Locale, out var en))
                    diagnostics.Add(Hint(rewardTypeNode!, en));

                CheckParams(eventNode, "Reward_Param", MaxRewardParamSlots,
                    rewardDef.Params, rewardType, gameIndex, diagnostics);
            }
        }
    }

    private void CheckParams(
        HtmlNode eventNode,
        string paramPrefix,
        int maxSlots,
        IReadOnlyList<ParamDefinition>? paramDefs,
        string typeName,
        GameIndex gameIndex,
        List<Diagnostic> diagnostics)
    {
        if (paramDefs is null) return;

        var maxDefinedPos = paramDefs.Count > 0 ? paramDefs.Max(p => p.Position) : -1;

        for (var n = 1; n <= maxSlots; n++)
        {
            var tagName = $"{paramPrefix}{n}";
            var child = FindChild(eventNode, tagName);
            if (child is null) continue;
            var value = child.InnerText.Trim();
            if (value.Length == 0) continue;

            var schemaPos = n - 1;

            if (schemaPos > maxDefinedPos)
            {
                diagnostics.Add(Warn(child, $"'{tagName}' is not used by {typeName}."));
                continue;
            }

            var paramDef = paramDefs.FirstOrDefault(p => p.Position == schemaPos);
            if (paramDef is null) continue;

            if (paramDef.Notes.TryGetValue(Locale, out var pn))
                diagnostics.Add(Hint(child, pn));

            var msg = ValidateParamValue(paramDef, value, gameIndex);
            if (msg is not null)
                diagnostics.Add(Warn(child, msg));
        }

        foreach (var p in paramDefs.Where(pd => !pd.Optional))
        {
            var tagName = $"{paramPrefix}{p.Position + 1}";
            var child = FindChild(eventNode, tagName);
            if (child is not null && child.InnerText.Trim().Length > 0) continue;
            diagnostics.Add(Warn(eventNode, $"{typeName} requires {tagName}."));
        }
    }

    private string? ValidateParamValue(ParamDefinition def, string value, GameIndex gameIndex)
    {
        return def.ValueType switch
        {
            XmlValueType.Int or XmlValueType.UInt =>
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    ? null : $"'{value}' is not a valid integer.",
            XmlValueType.Float =>
                float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    ? null : $"'{value}' is not a valid number.",
            XmlValueType.Boolean =>
                IsBooleanInt(value) ? null : $"'{value}' is not a valid boolean value. Use 0, 1, true, or false.",
            XmlValueType.DynamicEnumValue =>
                ValidateEnumList(def.EnumName, value),
            XmlValueType.NameReference =>
                ValidateSingleRef(def.ReferenceType, value, gameIndex),
            XmlValueType.NameReferenceList =>
                ValidateRefList(def.ReferenceType, value, gameIndex),
            XmlValueType.FloatVector3 =>
                ValidateFloatVector3(value),
            _ => null
        };
    }

    private static bool IsBooleanInt(string value)
        => value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("false", StringComparison.OrdinalIgnoreCase);

    private string? ValidateEnumList(string? enumName, string value)
    {
        if (enumName is null) return null;
        var enumDef = schema.GetEnum(enumName);
        if (enumDef is null) return null;
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!enumDef.Values.Any(v => string.Equals(v.Name, token, StringComparison.OrdinalIgnoreCase)))
                return $"'{token}' is not a valid {enumName} value.";
        }
        return null;
    }

    private static string? ValidateSingleRef(string? referenceType, string value, GameIndex gameIndex)
    {
        if (referenceType is null) return null;
        var sym = gameIndex.Resolve(value);
        if (sym is null)
            return $"'{value}' is not a recognized {referenceType}.";
        return null;
    }

    private static string? ValidateRefList(string? referenceType, string value, GameIndex gameIndex)
    {
        if (referenceType is null) return null;
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var sym = gameIndex.Resolve(token);
            if (sym is null)
                return $"'{token}' is not a recognized {referenceType}.";
        }
        return null;
    }

    private static string? ValidateFloatVector3(string value)
    {
        var parts = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return $"'{value}' is not a valid 3D vector. Expected three space- or comma-separated numbers.";
        foreach (var part in parts)
        {
            if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return $"'{part}' is not a valid number in vector '{value}'.";
        }
        return null;
    }

    private EnumValueDefinition? GetEventDef(string eventType)
        => schema.GetEnum("StoryEventType")?.Values
                 .FirstOrDefault(v => string.Equals(v.Name, eventType, StringComparison.OrdinalIgnoreCase));

    private EnumValueDefinition? GetRewardDef(string rewardType)
        => schema.GetEnum("StoryRewardType")?.Values
                 .FirstOrDefault(v => string.Equals(v.Name, rewardType, StringComparison.OrdinalIgnoreCase));

    private static HtmlNode? FindChild(HtmlNode parent, string tagName)
        => parent.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            string.Equals(n.Name, tagName, StringComparison.OrdinalIgnoreCase));

    private static Diagnostic Warn(HtmlNode node, string message) => new()
    {
        Severity = DiagnosticSeverity.Warning,
        Message = message,
        Range = new Range(
            new Position(Math.Max(0, node.Line - 1), 0),
            new Position(Math.Max(0, node.Line - 1), int.MaxValue)),
        Source = "pg-swg-lsp"
    };

    private static Diagnostic Hint(HtmlNode node, string message) => new()
    {
        Severity = DiagnosticSeverity.Hint,
        Message = message,
        Range = new Range(
            new Position(Math.Max(0, node.Line - 1), 0),
            new Position(Math.Max(0, node.Line - 1), int.MaxValue)),
        Source = "pg-swg-lsp"
    };
}
