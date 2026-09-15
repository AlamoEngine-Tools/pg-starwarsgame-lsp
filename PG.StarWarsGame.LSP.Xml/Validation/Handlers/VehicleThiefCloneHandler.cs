// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A <c>Vehicle_Thief_Inside_Clone</c> must be able to let the thief back out, and must not keep
///     <c>GARRISON_VEHICLE</c>.
/// </summary>
/// <remarks>
///     <para>
///         The ability half is an engine assert. <c>VehicleThiefBehaviorClass::Begin_Stealing_Vehicle</c>
///         asserts the clone has <c>EJECT_VEHICLE_THIEF</c> (<c>VehicleThiefBehavior.cpp:0x112</c>) and
///         activates the eject only when it does, so without it the thief can never get out. All 18
///         clones vanilla points at carry it.
///     </para>
///     <para>
///         The behaviour half is the tag's documented requirement. The capture never reads
///         <c>GARRISON_VEHICLE</c>, but it loads the thief into the vehicle's flagship container, and
///         <c>GarrisonableBehaviorClass::Garrison_Unit</c> loads garrisoned units into that same
///         container. Two vanilla clones - <c>F9TZ_Cloaking_Transport_Captured</c> and
///         <c>HAV_Juggernaut_Captured</c> - inherit the behaviour from their base and are reported; what
///         a garrison beside the thief actually does in play is not measured, so the message states the
///         requirement and the shared container rather than a failure.
///     </para>
///     <para>
///         Each half carries its own id, so either can be silenced alone.
///     </para>
/// </remarks>
public sealed class VehicleThiefCloneHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    private const string CloneTag = "Vehicle_Thief_Inside_Clone";
    private const string RequiredAbility = "EJECT_VEHICLE_THIEF";
    private const string ForbiddenBehavior = "GARRISON_VEHICLE";

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.VehicleThiefCloneAbility;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (!string.Equals(fact.Tag.Tag, CloneTag, StringComparison.OrdinalIgnoreCase)) return [];

        // No resolver means the question cannot be answered; silence beats guessing.
        if (ctx.Objects is null) return [];

        var value = fact.RawValue.Trim();
        if (value.Length == 0) return [];

        var resolved = ctx.Objects.Resolve(value);

        // An id nothing defines belongs to the unresolved-reference check.
        if (!resolved.Found) return [];

        var results = new List<XmlDiagnosticResult>(2);

        if (!ObjectAbilities.Has(resolved, RequiredAbility))
            results.Add(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{value}' has no {RequiredAbility} ability, so a thief who captures this vehicle " +
                "can never get out."));

        // Read off the effective object, so a behaviour inherited from the base counts - which is
        // exactly how both vanilla hits keep it.
        if (ObjectBehaviors.Has(resolved, ForbiddenBehavior))
            results.Add(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{value}' keeps the {ForbiddenBehavior} behaviour, which a capture clone must not have. " +
                "The thief rides in the same container garrisoned units are loaded into.",
                Id: DiagnosticIds.VehicleThiefCloneGarrison));

        return results;
    }
}