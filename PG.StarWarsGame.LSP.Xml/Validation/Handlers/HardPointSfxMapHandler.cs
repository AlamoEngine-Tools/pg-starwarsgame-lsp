// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class HardPointSfxMapHandler : CommaSeparatedPairHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.HardPointSfxMap;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var parts = SplitOnFirstComma(fact.RawValue.Trim());
        // element[1] (SFXEvent) is allowed to be empty; element[0] (hard point type) must be non-empty
        if (parts.Length != 2 || parts[0].Trim().Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{fact.RawValue.Trim()}' is not a valid hard point SFX map for <{fact.Tag.Tag}>. Expected: HardPointType, SFXEventName.")
            ];

        var hardPointType = parts[0].Trim();
        var sfxEventName = parts[1].Trim();
        var results = new List<XmlDiagnosticResult>();

        var hardPointEnum = ctx.Schema.GetEnum("HardPointType");
        if (hardPointEnum is not null)
        {
            var known = hardPointEnum.Values.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!known.Contains(hardPointType))
                results.Add(AtPairSlot(new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{hardPointType}' is not a known HardPointType value for <{fact.Tag.Tag}>."), fact, 0));
        }

        var sfxResult = TryValidateSfxEvent(sfxEventName, fact.Tag.Tag, ctx.Index);
        if (sfxResult is not null)
            results.Add(AtPairSlot(sfxResult, fact, 1));

        return results;
    }
}