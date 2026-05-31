// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class AbilitySfxMapHandler : CommaSeparatedPairHandlerBase
{
    public override XmlValueType? HandledValueType => XmlValueType.AbilitySfxMap;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.AbilitySfxMap)
            return [];

        var parts = SplitOnFirstComma(fact.RawValue.Trim());
        // element[1] (SFXEvent) is allowed to be empty; element[0] (ability code) must be non-empty
        if (parts.Length != 2 || parts[0].Trim().Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{fact.RawValue.Trim()}' is not a valid ability SFX map for <{fact.Tag.Tag}>. Expected: AbilityName, SFXEventName.")
            ];

        var abilityCode = parts[0].Trim();
        var sfxEventName = parts[1].Trim();
        var results = new List<XmlDiagnosticResult>();

        var abilityTypeSet = ctx.Schema.AllHardcodedSets
            .FirstOrDefault(s => s.Name.Equals("AbilityType", StringComparison.OrdinalIgnoreCase));
        if (abilityTypeSet is not null)
        {
            var known = abilityTypeSet.Values.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!known.Contains(abilityCode))
                results.Add(new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{abilityCode}' is not a known AbilityType value for <{fact.Tag.Tag}>."));
        }

        var sfxResult = TryValidateSfxEvent(sfxEventName, fact.Tag.Tag, ctx.Index);
        if (sfxResult is not null)
            results.Add(sfxResult);

        return results;
    }
}