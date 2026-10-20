// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A hardpoint declared destroyable that nothing can ever hit.
/// </summary>
/// <remarks>
///     <para>
///         Error, and not because the engine says so - it says nothing at all. That is the point:
///         the hardpoint appears in the UI, takes its share of the object's health, and cannot be
///         destroyed, so the author has no way to find out short of shooting at it.
///     </para>
///     <para>
///         Both shapes come from the damage-routing pass over the 2018 binary rather than from a
///         message, so the wording explains the routing instead of quoting an assertion.
///     </para>
/// </remarks>
public sealed class HardpointUnhittableHandler : XmlDiagnosticsHandler<HardpointUnhittableFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.HardpointCannotBeHit;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        HardpointUnhittableFact fact, DiagnosticsContext ctx)
    {
        var message = fact.SharedWith is { } first
            ? ctx.Messages.Format("HardpointUnhittable_SharesItsCollisionMeshWith", fact.HardpointId, first)
            : ctx.Messages.Format("HardpointUnhittable_IsDestroyableButNamesNo", fact.HardpointId);

        return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Error, message)];
    }
}