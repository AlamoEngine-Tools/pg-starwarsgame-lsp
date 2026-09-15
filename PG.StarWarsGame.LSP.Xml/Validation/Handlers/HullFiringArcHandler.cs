// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Says what the turret-extent tags do on a WEAPON unit without a turret: they are the hull's firing arc
///     (A5).
/// </summary>
/// <remarks>
///     <para>
///         <c>WeaponBehaviorClass::Is_In_Cone_Of_Fire</c> refuses a shot whose yaw exceeds
///         <c>Turret_Rotate_Extent_Degrees</c> or whose pitch exceeds <c>Turret_Elevate_Extent_Degrees</c> -
///         the deployed pair while deployed, and no pitch test at all with <c>Turret_XY_Only</c> - measured
///         from the object's own facing. Its only other reader is <c>TurretBehaviorClass</c>. Without the
///         TURRET behaviour, then, tags named for a turret restrict where the hull can shoot.
///     </para>
///     <para>
///         A hint, not a warning. The plan expected three vanilla objects; the corpus has 86 in foc and 41
///         in eaw - fighters, speeders and bombers whose guns point forward on purpose. The message states
///         the arc and stops. <c>Fires_Forward</c> skips the test and is <c>FiresForwardHandler</c>'s to
///         report. Yaw under 180 and pitch under 90 are the only values that restrict anything.
///     </para>
/// </remarks>
public sealed class HullFiringArcHandler : XmlDiagnosticsHandler<HullFiringArcFact>
{
    private const string WeaponBehavior = "WEAPON";
    private const string TurretBehavior = "TURRET";

    /// <summary>Tag, the bound past which it restricts, and whether it is a pitch bound.</summary>
    private static readonly (string Tag, double Unrestricted, bool Pitch)[] Extents =
    [
        ("Turret_Rotate_Extent_Degrees", 180, false),
        ("Turret_Elevate_Extent_Degrees", 90, true),
        ("Deployed_Turret_Rotate_Extent_Degrees", 180, false),
        ("Deployed_Turret_Elevate_Extent_Degrees", 90, true)
    ];

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.HullFiringArc;

    protected override IEnumerable<XmlDiagnosticResult> Handle(HullFiringArcFact fact, DiagnosticsContext ctx)
    {
        if (ctx.Objects is null) return [];

        var unit = ctx.Objects.Resolve(fact.ObjectId);
        if (!unit.Found) return [];

        var behaviours = ObjectBehaviors.Of(unit);
        if (!behaviours.Contains(WeaponBehavior) || behaviours.Contains(TurretBehavior)) return [];
        if (EngineBoolean.IsTrue(Value(unit, "Fires_Forward"))) return [];

        var xyOnly = EngineBoolean.IsTrue(Value(unit, "Turret_XY_Only"));
        var restricting = new List<string>();
        foreach (var (tag, unrestricted, pitch) in Extents)
        {
            if (pitch && xyOnly) continue;
            if (!double.TryParse(Value(unit, tag), NumberStyles.Float, CultureInfo.InvariantCulture, out var bound)) continue;
            if (bound >= unrestricted) continue;

            var text = bound.ToString("0.###", CultureInfo.InvariantCulture);
            restricting.Add(pitch
                ? $"{tag} {text} (up to {text} degrees up or down)"
                : $"{tag} {text} (up to {text} degrees either side)");
        }

        if (restricting.Count == 0) return [];

        var message =
            $"'{fact.ObjectId}' has no TURRET behaviour, so these limit where its hull can shoot, measured from " +
            $"its facing: {string.Join(", ", restricting)}.";

        return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Hint, message, Id: DiagnosticIds.HullFiringArc)];
    }

    private static string? Value(EffectiveObject obj, string tag)
    {
        return obj.Tags.LastOrDefault(t => t.TagName.Equals(tag, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
    }
}
