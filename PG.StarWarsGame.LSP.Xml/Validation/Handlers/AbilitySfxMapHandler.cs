// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class AbilitySfxMapHandler : CommaSeparatedPairHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.AbilitySfxMap;

    protected override XmlValueType TargetType => XmlValueType.AbilitySfxMap;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var parts = SplitOnFirstComma(fact.RawValue.Trim());
        // element[1] (SFXEvent) is allowed to be empty; element[0] (ability code) must be non-empty
        if (parts.Length != 2 || parts[0].Trim().Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    ctx.Messages.Format("AbilitySfxMap_IsNotAValidAbility", fact.RawValue.Trim(), fact.Tag.Tag))
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
                results.Add(AtPairSlot(new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    ctx.Messages.Format("AbilitySfxMap_IsNotAKnownAbilitytype", abilityCode, fact.Tag.Tag),
                    Id: DiagnosticIds.AbilitySfxMapUnknownAbilityType), fact, 0));
        }

        var sfxResult = TryValidateSfxEvent(sfxEventName, fact.Tag.Tag, ctx.Index, ctx.Messages);
        if (sfxResult is not null)
            results.Add(AtPairSlot(sfxResult, fact, 1));

        return results;
    }
}