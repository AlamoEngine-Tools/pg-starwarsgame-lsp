// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class SquadronOffsetsMismatchHandler : XmlDiagnosticsHandler<SquadronOffsetsMismatchFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.SquadronOffsetsMismatch;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        SquadronOffsetsMismatchFact fact, DiagnosticsContext ctx)
    {
        string message;
        if (fact.TotalOffsets < fact.TotalUnits)
        {
            var missing = fact.TotalUnits - fact.TotalOffsets;
            message = ctx.Messages.Format("SquadronOffsetsMismatch_SquadronHasUnitSBut", fact.TotalUnits,
                fact.TotalOffsets, missing);
        }
        else
        {
            var excess = fact.TotalOffsets - fact.TotalUnits;
            message = ctx.Messages.Format("SquadronOffsetsMismatch_SquadronHasUnitSBut2", fact.TotalUnits,
                fact.TotalOffsets, excess);
        }

        return
        [
            new XmlDiagnosticResult(
                XmlDiagnosticSeverity.Warning,
                message,
                SquadronSyncJson: $"{{\"expectedOffsets\":{fact.TotalUnits}}}")
        ];
    }
}