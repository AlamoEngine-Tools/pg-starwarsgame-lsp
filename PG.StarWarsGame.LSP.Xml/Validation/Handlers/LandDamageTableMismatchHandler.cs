// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports a <c>Land_Damage_*</c> table whose two positional columns are of different lengths.
/// </summary>
/// <remarks>
///     Warning rather than error, and reported on the LONGER column: that is where the entries with
///     no partner are, and it is the tag an author can act on. What the engine does with the surplus
///     is not measured, so the message says only what the document shows - how many entries each
///     column has, and how many of them pair with nothing.
/// </remarks>
public sealed class LandDamageTableMismatchHandler
    : XmlDiagnosticsHandler<LandDamageTableMismatchFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.LandDamageTableMismatch;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        LandDamageTableMismatchFact fact, DiagnosticsContext ctx)
    {
        var thresholdsLonger = fact.Thresholds > fact.Alternates;
        var longerTag = thresholdsLonger ? "Land_Damage_Thresholds" : "Land_Damage_Alternates";
        var (line, column, length) = thresholdsLonger
            ? fact.ThresholdsPosition
            : fact.AlternatesPosition;

        var keep = Math.Min(fact.Thresholds, fact.Alternates);
        var surplus = Math.Max(fact.Thresholds, fact.Alternates) - keep;
        var entries = surplus == 1 ? "entry" : "entries";

        var message =
            $"<Land_Damage_Thresholds> has {fact.Thresholds} and <Land_Damage_Alternates> has "
            + $"{fact.Alternates} {(fact.Alternates == 1 ? "entry" : "entries")}. The columns pair by "
            + $"position, so the last {surplus} {entries} of <{longerTag}> pair with nothing.";

        // Trimming to zero would empty the table rather than fix it, so an empty companion column
        // gets the report without a fix - what to write there is the author's decision.
        var trimmed = keep == 0
            ? null
            : string.Join(", ", (thresholdsLonger ? fact.ThresholdEntries : fact.AlternateEntries)
                .Take(keep));

        return
        [
            new XmlDiagnosticResult(
                XmlDiagnosticSeverity.Warning,
                message,
                line,
                column,
                length,
                trimmed)
        ];
    }
}
