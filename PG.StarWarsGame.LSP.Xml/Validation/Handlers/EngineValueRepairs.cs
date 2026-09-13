// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     The value the engine assigns to a numeric tag after it complains about one.
/// </summary>
/// <remarks>
///     <para>
///         Keyed on <c>(owning type, tag)</c>, because neither half identifies a repair on its own.
///         One range rule serves many owners - <c>non-negative-value</c> covers 27 tags - and the
///         repairs differ: <c>fraction-below-one</c> applies to <c>Time_Reduction_Percentage</c>,
///         which the engine resets to zero, and to <c>Owner_Income_Percentage</c>, which it clamps
///         to 0.99. A repair on the handler would be right for one owner and wrong for the next.
///     </para>
///     <para>
///         <b>Every entry is the assignment following the message in the 2018 binary</b>, with the
///         address of the validator beside it. A tag with no entry offers no fix, and that is the
///         correct default: the repair is not implied by the bound, so an unmeasured tag has no
///         answer to give. Do not add a row by reasoning from the range.
///     </para>
///     <para>
///         Deliberately not in the schema. The schema states what is legal, and this states what
///         the engine does about the illegal - it never changes whether a document validates. It is
///         also a set of measurements against one build, due to be re-read against the 64-bit one,
///         which is not something to version into a hashed, shipped contract.
///     </para>
/// </remarks>
internal static class EngineValueRepairs
{
    private static readonly Dictionary<(string Owner, string Tag), string> Repairs =
        new(OwnerTagComparer.Instance)
        {
            // LeechShieldsAbilityClass::Validate_Data (01028000)
            [("LeechShieldsAbility", "Activation_Min_Range")] = "0.0",
            [("LeechShieldsAbility", "Activation_Max_Range")] = "0.0",
            [("LeechShieldsAbility", "Duration_In_Secs")] = "1.0",

            // PoliticalTransitionBonusAbilityClass::Validate_Data (01016100) - reset, not clamped
            // to the nearest legal value.
            [("PoliticalTransitionBonusAbility", "Time_Reduction_Percentage")] = "0.0",

            // IncomeStreamAbilityClass::Validate_Data (0100f6a0)
            [("IncomeStreamAbility", "Base_Interval_In_Secs")] = "1.0",
        };

    /// <summary>
    ///     The engine's replacement value, or null where none was measured for this pair.
    /// </summary>
    public static string? For(string? owningType, string tag)
    {
        if (string.IsNullOrEmpty(owningType)) return null;

        return Repairs.TryGetValue((owningType, tag), out var repair) ? repair : null;
    }

    /// <summary>Case-insensitive on both halves, matching how the schema and the XML are compared.</summary>
    private sealed class OwnerTagComparer : IEqualityComparer<(string Owner, string Tag)>
    {
        public static readonly OwnerTagComparer Instance = new();

        public bool Equals((string Owner, string Tag) x, (string Owner, string Tag) y)
        {
            return string.Equals(x.Owner, y.Owner, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.Tag, y.Tag, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Owner, string Tag) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Owner),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Tag));
        }
    }
}
