// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A flag switched on without the tag the engine needs beside it.
/// </summary>
/// <remarks>
///     Error rather than warning, matching the engine's own label and because the outcome is a
///     silent behaviour change rather than a cosmetic one: the object loads with a setting the
///     author did not write, and the only way to find out is to play it.
/// </remarks>
public sealed class BooleanGatedRequirementHandler : XmlDiagnosticsHandler<BooleanGatedRequirementFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.BooleanGatedRequirement;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        BooleanGatedRequirementFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"<{fact.GateTag}> is on, so <{fact.RequiredTag}> must be {fact.Requirement} - " +
                $"the engine otherwise {fact.Repair}.")
        ];
    }
}
