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
///         Warning rather than error: the reference resolves and the file loads, but the weapon
///         will not behave as one in a standalone battle.
///     </para>
/// </remarks>
public sealed class SpecialWeaponBehaviorHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    private const string RequiredBehavior = "SPECIAL_WEAPON";

    private static readonly string[] WeaponTags =
    [
        "Standalone_Space_Maps_Special_Weapon_A",
        "Standalone_Space_Maps_Special_Weapon_B"
    ];

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.SpecialWeaponBehavior;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (!WeaponTags.Contains(fact.Tag.Tag, StringComparer.OrdinalIgnoreCase)) return [];

        // No resolver means the question cannot be answered here. Silence is the honest answer:
        // treating "cannot tell" as "fails" would report every faction in a narrow context.
        if (ctx.Objects is null) return [];

        var value = fact.RawValue.Trim();
        if (value.Length == 0) return [];

        var resolved = ctx.Objects.Resolve(value);

        // An id nothing defines belongs to the unresolved-reference check. Reporting it again as a
        // behaviour fault would put two diagnostics on one typo and name the wrong cause.
        if (!resolved.Found) return [];

        if (ObjectBehaviors.Has(resolved, RequiredBehavior)) return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{value}' is not a special weapon - it has no {RequiredBehavior} behaviour, so it " +
                "will not fire in a standalone battle.")
        ];
    }
}
