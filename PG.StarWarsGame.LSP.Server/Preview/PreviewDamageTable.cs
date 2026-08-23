// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     <c>Damage_To_Armor_Mod</c> from GameConstants: how much a damage type is worth against an
///     armor type.
/// </summary>
/// <remarks>
///     <para>
///         Rows of <c>&lt;Damage_To_Armor_Mod&gt; Damage_X, Armor_Y, 1.5 &lt;/...&gt;</c> - 2426 of
///         them in foc over 81 damage types and 53 armor types, 1654 in eaw. 81 x 53 is 4293, so the
///         table names barely half the pairs it could. That gap is not an authoring mistake and must
///         not be reported as one: <strong>a missing pair is 1.0</strong>.
///     </para>
///     <para>
///         Read through <see cref="RepeatedTagReader" />, exactly as the reticle rows are. The
///         effective-object resolver collapses a repeated tag to its last occurrence, so resolving
///         GameConstants yields ONE row out of 2426.
///     </para>
/// </remarks>
public sealed class PreviewDamageTable
{
    private const string TagName = "Damage_To_Armor_Mod";

    /// <summary>
    ///     The declared list of damage types, which is a different thing from the factor matrix.
    /// </summary>
    /// <remarks>
    ///     One comma-separated list rather than a repeated row. The shipped file's own comment
    ///     splits it: everything below a marked point is "from hard-coded damage enumeration", and
    ///     none of that tail - <c>Damage_Normal</c>, <c>Damage_Fire</c>, <c>Damage_Crush</c> and the
    ///     rest - has a single <c>Damage_To_Armor_Mod</c> row anywhere in foc.
    /// </remarks>
    private const string ListTagName = "Damage_Types";

    /// <summary>Factor by armor type, then by damage type. Both axes are case-insensitive.</summary>
    private readonly Dictionary<string, Dictionary<string, float>> _byArmor;

    private PreviewDamageTable(
        Dictionary<string, Dictionary<string, float>> byArmor, IReadOnlyList<string> damageTypes)
    {
        _byArmor = byArmor;
        DamageTypes = damageTypes;
    }

    /// <summary>
    ///     Every damage type the table mentions, sorted, for the attacker panel's picker.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         All of them, not only those with a row against the target being shown. A type with no
    ///         pair is a perfectly ordinary choice that resolves to 1.0, and leaving it out of the
    ///         picker would hide exactly the case a modder needs to be able to try.
    ///     </para>
    ///     <para>
    ///         The UNION of <c>Damage_Types</c> and the matrix, because neither list contains the
    ///         other. Reading only the matrix - which is what this did - dropped every hard-coded
    ///         type, <c>Damage_Normal</c> included; and <c>Damage_Normal</c> is what the Star
    ///         Destroyer names in its <c>Death_Clone</c>, so its wreck could not be produced at all.
    ///         Reading only the list would drop a type a mod adds a factor for without touching it.
    ///     </para>
    /// </remarks>
    public IReadOnlyList<string> DamageTypes { get; }

    public static PreviewDamageTable From(GameIndex index, IVariantTagSource tagSource)
    {
        var byArmor = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
        var damageTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        // `Values`, not `Rows`: this is ONE tag holding a comma-separated list of 81, where the
        // matrix is 2426 tags of three fields each. A row reader would drop it for having the wrong
        // field count, which is exactly the shape of silence this bug arrived as.
        foreach (var value in RepeatedTagReader.Values(
                     index, tagSource, EncyclopediaTags.GameConstantsId, ListTagName))
        {
            foreach (var name in value.Split(',', StringSplitOptions.TrimEntries
                                                  | StringSplitOptions.RemoveEmptyEntries))
                damageTypes.Add(name);
        }

        foreach (var row in RepeatedTagReader.Rows(
                     index, tagSource, EncyclopediaTags.GameConstantsId, TagName, 3))
        {
            // A factor that is not a number is a broken row, not a zero. Dropping it leaves the pair
            // at its 1.0 default, which is what the engine would do with a value it cannot read.
            if (!float.TryParse(row[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var factor))
                continue;

            if (row[0].Length == 0 || row[1].Length == 0)
                continue;

            damageTypes.Add(row[0]);

            if (!byArmor.TryGetValue(row[1], out var forArmor))
            {
                forArmor = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                byArmor[row[1]] = forArmor;
            }

            // Last row wins, matching the engine's own read of a file that names a pair twice.
            forArmor[row[0]] = factor;
        }

        return new PreviewDamageTable(byArmor, [.. damageTypes]);
    }

    /// <summary>
    ///     Every factor declared against one armor type, keyed by damage type.
    /// </summary>
    /// <remarks>
    ///     Only the pairs that EXIST. Filling the gaps with 1.0 here would make the wire claim the
    ///     table says something it does not, and it is the client that applies the default - one
    ///     place, next to the arithmetic that uses it.
    /// </remarks>
    public IReadOnlyDictionary<string, float> FactorsFor(string? armorType)
    {
        if (string.IsNullOrWhiteSpace(armorType))
            return new Dictionary<string, float>();

        return _byArmor.TryGetValue(armorType.Trim(), out var factors)
            ? factors
            : new Dictionary<string, float>();
    }
}
