// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A <c>Vehicle_Thief_Inside_Clone</c> must be able to let the thief back out.
/// </summary>
/// <remarks>
///     <para>
///         The tag description states two conditions. Only the ability one is enforced. All 18
///         clones vanilla points at carry <c>EJECT_VEHICLE_THIEF</c>, so the rule fires on nothing
///         shipped, and it is mechanically necessary regardless: without it the captured vehicle
///         can never release its thief, which is the clone's entire purpose.
///     </para>
///     <para>
///         The <c>GARRISON_VEHICLE</c> half is deliberately left alone. Two of those 18 clones -
///         <c>F9TZ_Cloaking_Transport_Captured</c> and <c>HAV_Juggernaut_Captured</c> - declare no
///         behaviour tag of their own, so they inherit it from their base and the effective object
///         really does keep it. Nothing available says that breaks anything: the GlyphX release
///         carries the Lua garrison wrapper but not the capture mechanic. Enforcing it would mean
///         warning on shipped data on the strength of a description alone, which is the mistake
///         #98 was.
///     </para>
/// </remarks>
public sealed class VehicleThiefCloneHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    private const string CloneTag = "Vehicle_Thief_Inside_Clone";
    private const string RequiredAbility = "EJECT_VEHICLE_THIEF";

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

        if (ObjectAbilities.Has(resolved, RequiredAbility)) return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{value}' has no {RequiredAbility} ability, so a thief who captures this vehicle " +
                "can never get out.")
        ];
    }
}
