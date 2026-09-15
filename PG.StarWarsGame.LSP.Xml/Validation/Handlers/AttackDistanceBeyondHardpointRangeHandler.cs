// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A unit that fires through hardpoints is told to attack from farther than any of them reaches
///     (#101).
/// </summary>
/// <remarks>
///     <para>
///         Measured in the 2018 build. Movement closes to <c>Targeting_Max_Attack_Distance</c> plus the
///         target's size - <c>MovementCoordinatorClass::Compute_Targeting_Approach_Distance</c> - and
///         <c>HardPointClass::Attempt_Fire_At_Target</c> refuses a shot beyond the hardpoint's own
///         <c>Fire_Range_Distance</c> plus the target's soft radius. With the first above the second the
///         unit can stop where no hardpoint fires. The two pads differ and the hardpoint sits off the
///         unit's centre, so the message says CAN, not WILL. <c>Fire_Range_Distance</c> defaults to 0 in
///         the <c>HardPointDataClass</c> constructor, which is how an unwritten one is read here.
///     </para>
///     <para>
///         Out of scope: a unit with the WEAPON behaviour. <c>ProjectileFiringProperties</c> takes the
///         attack distance as the shot's <c>MaxFlightDistance</c>, so on that path it is the weapon's range
///         and cannot outrange it - the ticket's "max weapon range" half is true by construction.
///     </para>
///     <para>
///         Measured over eaw/ and foc/: the attack distance EQUALS the longest weapon-hardpoint range on
///         22 and 43 units - the shipped convention - and exceeds it on 3 and 4, among them
///         <c>Tantive_IV</c> at 2000 against 800.
///     </para>
/// </remarks>
public sealed class AttackDistanceBeyondHardpointRangeHandler : XmlDiagnosticsHandler<AttackDistanceFact>
{
    private const string AttackDistanceTag = "Targeting_Max_Attack_Distance";
    private const string HardpointsTag = "HardPoints";
    private const string RangeTag = "Fire_Range_Distance";
    private const string TypeTag = "Type";
    private const string WeaponTypePrefix = "HARD_POINT_WEAPON";
    private const string WeaponBehavior = "WEAPON";

    private static readonly char[] ListSeparators = [',', ' ', '\t', '\n', '\r'];

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.AttackDistanceBeyondHardpointRange;

    protected override IEnumerable<XmlDiagnosticResult> Handle(AttackDistanceFact fact, DiagnosticsContext ctx)
    {
        // No resolver, or a unit it cannot find: the values are unknown, and silence is the honest answer.
        if (ctx.Objects is null) return [];

        var unit = ctx.Objects.Resolve(fact.ObjectId);
        if (!unit.Found) return [];

        // On the WEAPON path the attack distance is the shot's own flight distance.
        if (ObjectBehaviors.Has(unit, WeaponBehavior)) return [];

        if (!TryNumber(Value(unit, AttackDistanceTag), out var attackDistance)) return [];

        var hardpoints = Value(unit, HardpointsTag);
        if (hardpoints is null) return [];

        string? longestId = null;
        string? longestText = null;
        var longest = 0.0;
        foreach (var hardpointId in hardpoints.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var hardpoint = ctx.Objects.Resolve(hardpointId);
            if (!hardpoint.Found) continue;

            var type = Value(hardpoint, TypeTag);
            if (type is null || !type.StartsWith(WeaponTypePrefix, StringComparison.OrdinalIgnoreCase)) continue;

            // Unwritten reads as the constructor's 0.0, which can never be the longest.
            var rangeText = Value(hardpoint, RangeTag);
            if (!TryNumber(rangeText, out var range) || range <= longest) continue;

            longest = range;
            longestId = hardpointId;
            longestText = rangeText;
        }

        if (longestId is null || attackDistance <= longest) return [];

        var message =
            $"'{fact.ObjectId}' closes only to Targeting_Max_Attack_Distance {Format(attackDistance)}, but its " +
            $"longest-reaching hardpoint weapon '{longestId}' fires to {Format(longest)}, so it can stop where " +
            "no hardpoint fires.";

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, message,
                SuggestedFix: fact.AnchoredOnAttackDistance ? longestText : null,
                Id: DiagnosticIds.AttackDistanceBeyondHardpointRange,
                FixTitle: fact.AnchoredOnAttackDistance ? $"Set to the longest hardpoint range ({longestText})" : null)
        ];
    }

    private static string? Value(EffectiveObject obj, string tag)
    {
        return obj.Tags.LastOrDefault(t => t.TagName.Equals(tag, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
    }

    private static bool TryNumber(string? text, out double value)
    {
        value = 0;
        return text is not null
               && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}