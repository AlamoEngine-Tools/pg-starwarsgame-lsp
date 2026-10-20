// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports a hardpoint whose rocket-attack flags leave it no state to fire in. An error: the
///     weapon is declared, costs its hardpoint, and never fires.
/// </summary>
public sealed class HardpointNeverFiresHandler : XmlDiagnosticsHandler<HardpointNeverFiresFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.HardpointNeverFires;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        HardpointNeverFiresFact fact, DiagnosticsContext ctx)
    {
        // Both flags off holds for any owner, so that finding names none.
        var message = fact.OwnerId is null
            ? ctx.Messages.Format("HardpointNeverFires_HardpointNeverFiresFireWhen", fact.HardpointId)
            : ctx.Messages.Format("HardpointNeverFires_HardpointNeverFiresOnFire", fact.HardpointId, fact.OwnerId,
                fact.OwnerId);

        return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Error, message)];
    }
}