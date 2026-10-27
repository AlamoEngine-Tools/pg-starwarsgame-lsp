// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>A damage type list too short for the engine's tail: the game crashes reading it.</summary>
public sealed class DamageTypesIncompleteHandler : XmlDiagnosticsHandler<DamageTypesIncompleteFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.DamageTypesIncomplete;

    protected override IEnumerable<XmlDiagnosticResult> Handle(DamageTypesIncompleteFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                ctx.Messages.Format("DamageTypes_TooShort", fact.Count, fact.Required))
        ];
    }
}