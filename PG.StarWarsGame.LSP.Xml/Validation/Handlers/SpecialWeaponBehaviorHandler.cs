// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A faction's standalone-map special weapon must name an object that actually is one.
/// </summary>
/// <remarks>
///     <para>
///         Issue #98 asked for a name whitelist - "Hypervelocity Cannon or Ion Cannon". That rule
///         is wrong and vanilla proves it: the shipped values are <c>Ground_Ion_Cannon</c> and
///         <c>Ground_Empire_Hypervelocity_Gun</c>, so it would fire on the base game, and the
///         reporter withdrew it in the comments once they noticed EaWX uses the same
///         <c>Ground_</c> prefix. Their revised rule - check the behaviour - is what this
///         implements, and both vanilla weapons carry <c>SPECIAL_WEAPON</c>.
///     </para>
///     <para>
///         Measured in the 2018 build: <c>GameModeClass::Add_Special_Weapon</c> registers the weapon
///         only if it behaves like <c>SPECIAL_WEAPON</c> or <c>LOBBING_SUPERWEAPON</c>, and otherwise
///         returns false without an assert - the weapon is never registered, so there is nothing to
///         fire. <see cref="SpecialWeaponIndexHandler" /> covers the index test on the same path.
///     </para>
///     <para>
///         Warning rather than error: the reference resolves and the file loads, but the weapon
///         is silently missing in a standalone battle.
///     </para>
///     <para>
///         A only. The engine builds B too, but the command bar reads only A, so B never gets a button
///         and cannot be fired - its deprecation is the warning it gets.
///     </para>
/// </remarks>
public sealed class SpecialWeaponBehaviorHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    private const string WeaponTag = "Standalone_Space_Maps_Special_Weapon_A";

    private static readonly string[] AcceptedBehaviors = ["SPECIAL_WEAPON", "LOBBING_SUPERWEAPON"];

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.SpecialWeaponBehavior;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (!string.Equals(fact.Tag.Tag, WeaponTag, StringComparison.OrdinalIgnoreCase)) return [];

        // No resolver means the question cannot be answered here. Silence is the honest answer:
        // treating "cannot tell" as "fails" would report every faction in a narrow context.
        if (ctx.Objects is null) return [];

        var value = fact.RawValue.Trim();
        if (value.Length == 0) return [];

        var resolved = ctx.Objects.Resolve(value);

        // An id nothing defines belongs to the unresolved-reference check. Reporting it again as a
        // behaviour fault would put two diagnostics on one typo and name the wrong cause.
        if (!resolved.Found) return [];

        if (AcceptedBehaviors.Any(behavior => ObjectBehaviors.Has(resolved, behavior))) return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{value}' has neither the SPECIAL_WEAPON nor the LOBBING_SUPERWEAPON behaviour, so the " +
                "game never registers it as a special weapon.")
        ];
    }
}