// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     What turning <c>Fires_Forward</c> on actually changes, judged on the effective object.
/// </summary>
/// <remarks>
///     <para>
///         <c>GameObjectTypeClass::Get_Fires_Forward</c> has one caller,
///         <c>WeaponBehaviorClass::Calculate_Projectile_Facing</c>, reached only from the WEAPON
///         behaviour's <c>Fire_Projectile</c>. Hardpoints fire through <c>HardPoint.cpp</c> and never call
///         it. So on an object without WEAPON the flag does nothing - zero such objects in eaw/ and foc/.
///     </para>
///     <para>
///         With WEAPON it returns before <c>Is_In_Cone_Of_Fire</c>, the firing-side reader of the four
///         turret-extent tags, so those stop limiting the shot. <c>TurretBehaviorClass</c> reads the same
///         tags to swing the turret, which is why the message changes when the object has a TURRET.
///         Vanilla does this once per game (<c>Y-Wing_Bombing_Run</c>) with a comment saying it means to.
///     </para>
///     <para>
///         Both are hints: neither is wrong, each is a tag that reads as doing something it does not.
///     </para>
/// </remarks>
public sealed class FiresForwardHandler : XmlDiagnosticsHandler<FiresForwardFact>
{
    private const string WeaponBehavior = "WEAPON";
    private const string TurretBehavior = "TURRET";

    private static readonly string[] ExtentTags =
    [
        "Turret_Rotate_Extent_Degrees",
        "Turret_Elevate_Extent_Degrees",
        "Deployed_Turret_Rotate_Extent_Degrees",
        "Deployed_Turret_Elevate_Extent_Degrees"
    ];

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.FiresForwardWithoutWeapon;

    protected override IEnumerable<XmlDiagnosticResult> Handle(FiresForwardFact fact, DiagnosticsContext ctx)
    {
        // No resolver, or an object it cannot find: the behaviours are unknown, and silence is the
        // honest answer.
        if (ctx.Objects is null) return [];

        var resolved = ctx.Objects.Resolve(fact.ObjectId);
        if (!resolved.Found) return [];

        var behaviours = ObjectBehaviors.Of(resolved);

        if (!behaviours.Contains(WeaponBehavior))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Hint,
                    $"Fires_Forward does nothing on '{fact.ObjectId}': only the {WeaponBehavior} behaviour " +
                    "reads it, and this object has none.",
                    Id: DiagnosticIds.FiresForwardWithoutWeapon)
            ];

        var extents = ExtentTags
            .Where(tag => resolved.Tags.Any(t => t.TagName.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (extents.Count == 0) return [];

        var named = string.Join(" and ", extents);
        var message = behaviours.Contains(TurretBehavior)
            ? $"Fires_Forward skips the firing-arc check, so {named} still swing the turret but no longer " +
              $"limit where '{fact.ObjectId}' fires."
            : $"Fires_Forward skips the firing-arc check, so {named} no longer limit where " +
              $"'{fact.ObjectId}' fires.";

        return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Hint, message, Id: DiagnosticIds.FiresForwardIgnoresArc)];
    }
}
