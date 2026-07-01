// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class DamageToArmorModHandler : SingleValueTypeHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.DamageToArmorMod;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        var parts = trimmed.Split(',');
        if (parts.Length != 3 ||
            parts[0].Trim().Length == 0 ||
            parts[1].Trim().Length == 0 ||
            !LenientFloatParser.TryParse(parts[2].Trim(), out _))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid damage-to-armor modifier for <{fact.Tag.Tag}>. Expected: DamageType, ArmorType, Float.")
            ];

        var results = new List<XmlDiagnosticResult>();

        var knownDamageTypes = MergedEnumValues(ctx, "DamageType");
        if (knownDamageTypes is not null)
        {
            var damageValue = parts[0].Trim();
            if (!knownDamageTypes.Contains(damageValue))
                results.Add(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"'{damageValue}' is not a known DamageType value."));
        }

        var knownArmorTypes = MergedEnumValues(ctx, "ArmorType");
        if (knownArmorTypes is not null)
        {
            var armorValue = parts[1].Trim();
            if (!knownArmorTypes.Contains(armorValue))
                results.Add(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"'{armorValue}' is not a known ArmorType value."));
        }

        return results;
    }

    // Returns null when neither source has any values (skip validation).
    private static HashSet<string>? MergedEnumValues(DiagnosticsContext ctx, string enumName)
    {
        ctx.Index.Baseline.DynamicEnumValues.TryGetValue(enumName, out var baseline);
        ctx.Index.WorkspaceDynamicEnumValues.TryGetValue(enumName, out var workspace);
        if (baseline.IsDefaultOrEmpty && workspace.IsDefaultOrEmpty) return null;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!baseline.IsDefaultOrEmpty) foreach (var v in baseline) set.Add(v);
        if (!workspace.IsDefaultOrEmpty) foreach (var v in workspace) set.Add(v);
        return set;
    }
}
