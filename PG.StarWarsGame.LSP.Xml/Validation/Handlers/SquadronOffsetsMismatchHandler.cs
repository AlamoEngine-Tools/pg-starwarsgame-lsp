// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class SquadronOffsetsMismatchHandler : XmlDiagnosticsHandler<SquadronOffsetsMismatchFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(
        SquadronOffsetsMismatchFact fact, DiagnosticsContext ctx)
    {
        string message;
        if (fact.TotalOffsets < fact.TotalUnits)
        {
            var missing = fact.TotalUnits - fact.TotalOffsets;
            message = $"Squadron has {fact.TotalUnits} unit(s) but only {fact.TotalOffsets} Squadron_Offsets. " +
                      $"Add {missing} more.";
        }
        else
        {
            var excess = fact.TotalOffsets - fact.TotalUnits;
            message = $"Squadron has {fact.TotalUnits} unit(s) but {fact.TotalOffsets} Squadron_Offsets. " +
                      $"Remove {excess} excess.";
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
