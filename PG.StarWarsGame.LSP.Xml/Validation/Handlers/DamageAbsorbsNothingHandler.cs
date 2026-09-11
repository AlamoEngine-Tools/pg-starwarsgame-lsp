// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A warning, not an error: the file is valid and the game will load it. The ability simply
///     never absorbs anything, which is a design mistake rather than a broken definition.
/// </summary>
public sealed class DamageAbsorbsNothingHandler : XmlDiagnosticsHandler<DamageAbsorbsNothingFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.DamageAbsorbsNothing;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        DamageAbsorbsNothingFact fact, DiagnosticsContext ctx)
    {
        // Naming the missing half is the actionable part - which tag to go and set.
        var missing = fact.Percentage is null
            ? "Damage_Absorb_Percentage"
            : fact.Amount is null
                ? "Damage_Absorb_Amount"
                : null;

        var state = missing is null
            ? $"Both are {Show(fact.Percentage)}"
            : $"{Show(fact.Percentage ?? fact.Amount)} with no {missing}";

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"This absorbs no damage. {state}, and the amount healed is " +
                "(damage * Damage_Absorb_Percentage) + Damage_Absorb_Amount, so one of them must " +
                "be non-zero.")
        ];
    }

    private static string Show(double? value)
    {
        return (value ?? 0).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
