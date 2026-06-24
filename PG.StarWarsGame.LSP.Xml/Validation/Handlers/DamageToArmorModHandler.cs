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

        if (ctx.Index.Baseline.DynamicEnumValues.TryGetValue("DamageType", out var damageTypes) && damageTypes.Length > 0)
        {
            var damageValue = parts[0].Trim();
            if (!new HashSet<string>(damageTypes, StringComparer.OrdinalIgnoreCase).Contains(damageValue))
                results.Add(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"'{damageValue}' is not a known DamageType value."));
        }

        if (ctx.Index.Baseline.DynamicEnumValues.TryGetValue("ArmorType", out var armorTypes) && armorTypes.Length > 0)
        {
            var armorValue = parts[1].Trim();
            if (!new HashSet<string>(armorTypes, StringComparer.OrdinalIgnoreCase).Contains(armorValue))
                results.Add(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"'{armorValue}' is not a known ArmorType value."));
        }

        return results;
    }
}
