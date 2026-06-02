// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class AbilityModFlagHandler : CommaSeparatedPairHandlerBase
{
    private static readonly HashSet<string> ValidBoolValues =
        new(StringComparer.OrdinalIgnoreCase) { "true", "false", "yes", "no", "1", "0" };

    protected override XmlValueType TargetType => XmlValueType.AbilityModFlag;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var parts = SplitOnFirstComma(fact.RawValue.Trim());
        if (parts.Length != 2 || parts[0].Trim().Length == 0 ||
            !ValidBoolValues.Contains(parts[1].Trim()))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{fact.RawValue.Trim()}' is not a valid ability flag for <{fact.Tag.Tag}>. Expected: FlagType, Boolean.")
            ];

        return [];
    }
}