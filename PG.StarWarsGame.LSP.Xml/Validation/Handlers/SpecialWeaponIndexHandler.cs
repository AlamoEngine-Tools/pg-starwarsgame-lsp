// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A faction's standalone special weapon must name an object with a <c>Special_Weapon_Index</c> of 0 to 2.
/// </summary>
/// <remarks>
///     <para>
///         Measured in the 2018 build (issue #98). Both paths that create the weapon test
///         <c>Get_Special_Weapon_Index() &gt;= 0</c> and assert when it is not;
///         <c>GameModeClass::Add_Special_Weapon</c> then asserts the index is below
///         <c>Get_Max_Special_Weapons</c>, which returns 3. Outside 0 to 2 the weapon is never registered.
///         The type's constructor sets the index to -1, so an object that never writes the tag fails.
///     </para>
///     <para>
///         The tag description's "faction must be able to build object type" is enforced by no reader and
///         is deliberately not checked.
///     </para>
///     <para>
///         A only. The engine builds B through the same two paths, but the command bar reads only A, so B
///         never gets a button and cannot be fired - its deprecation is the warning it gets.
///     </para>
/// </remarks>
public sealed class SpecialWeaponIndexHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    private const string WeaponTag = "Standalone_Space_Maps_Special_Weapon_A";
    private const string IndexTag = "Special_Weapon_Index";

    /// <summary><c>GameModeClass::Get_Max_Special_Weapons</c>.</summary>
    private const int MaxSpecialWeapons = 3;

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.SpecialWeaponIndex;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (!string.Equals(fact.Tag.Tag, WeaponTag, StringComparison.OrdinalIgnoreCase)) return [];
        if (ctx.Objects is null) return [];

        var value = fact.RawValue.Trim();
        if (value.Length == 0) return [];

        var resolved = ctx.Objects.Resolve(value);
        if (!resolved.Found) return [];

        var text = resolved.ValueOf(IndexTag);
        if (text is null)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"'{value}' has no {IndexTag}, so the game never registers it as a special weapon.",
                    Id: DiagnosticIds.SpecialWeaponIndex)
            ];

        // Not a number is the value validator's finding.
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) return [];
        if (index is >= 0 and < MaxSpecialWeapons) return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{value}' has {IndexTag} {text}, outside 0 to {MaxSpecialWeapons - 1}, so the game never " +
                "registers it as a special weapon.",
                Id: DiagnosticIds.SpecialWeaponIndex)
        ];
    }
}
