// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     An automatic ability that also despawns its owner.
/// </summary>
/// <remarks>
///     Error, on the same reasoning as the other repairs: the engine does not refuse the object, it
///     turns the flag off and carries on, so the ability ships doing something the author did not
///     ask for and the file still says otherwise.
/// </remarks>
public sealed class AutomaticDespawnHandler : XmlDiagnosticsHandler<AutomaticDespawnFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.AutomaticAbilityDespawn;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        AutomaticDespawnFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                ctx.Messages.Format("AutomaticDespawn_CausesDespawnIsOnUnder", fact.ActivationStyle),
                EngineRepair: fact.Repair is null
                    ? null
                    : new XmlEngineRepair(
                        ctx.Messages.Format("AutomaticDespawn_ApplyTheEngineSCorrection"),
                        [fact.Repair]))
        ];
    }
}