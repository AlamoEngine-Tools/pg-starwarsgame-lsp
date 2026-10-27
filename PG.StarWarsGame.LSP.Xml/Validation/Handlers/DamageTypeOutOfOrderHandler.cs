// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>A damage type out of its place in the engine's tail: the game reads the wrong one there.</summary>
public sealed class DamageTypeOutOfOrderHandler : XmlDiagnosticsHandler<DamageTypeOutOfOrderFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.DamageTypesOutOfOrder;

    protected override IEnumerable<XmlDiagnosticResult> Handle(DamageTypeOutOfOrderFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                ctx.Messages.Format("DamageTypes_OutOfOrder", fact.Token, fact.Expected, fact.Required))
        ];
    }
}