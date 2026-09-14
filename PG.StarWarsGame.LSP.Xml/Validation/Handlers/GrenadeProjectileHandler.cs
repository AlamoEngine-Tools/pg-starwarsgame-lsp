// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A grenade or remote-bomb ability must name a projectile that IS a grenade.
/// </summary>
/// <remarks>
///     <para>
///         From the ASSERT seam rather than an engine message - these two say nothing at runtime, so
///         neither message harvest could have found them.
///         <c>Abilities\GrenadeAttackAbility.cpp:440</c> (<c>0100bcad</c>) and
///         <c>Abilities\RemoteBombAbility.cpp:388</c> (<c>0102b056</c>) both read
///         <c>!type-&gt;Is_Projectile_Grenade()</c>, and the branch settles the polarity: the assert
///         fires when the call returns FALSE, so the text states the FAILURE and the rule is its
///         opposite. Reading the expression alone would have produced exactly the wrong rule.
///     </para>
///     <para>
///         The property is one comparison - <c>GameObjectTypeClass::Is_Projectile_Grenade</c>
///         (<c>009a7520</c>) is <c>ProjCategory == PROJECTILE_CATEGORY_GRENADE</c> - so the check is
///         the target's <c>Projectile_Category</c> and nothing more.
///     </para>
///     <para>
///         Error rather than warning: in the Internal build the assert is followed by
///         <c>XOR AL,AL; JMP</c>, so the call returns false and the ability does not fire. Whether
///         Gold keeps that abort is INFERRED from the macro shape rather than measured - the Gold
///         function was not located - so the message says what the file is wrong about rather than
///         promising a specific runtime outcome.
///     </para>
/// </remarks>
public sealed class GrenadeProjectileHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    private const string CategoryTag = "Projectile_Category";
    private const string RequiredCategory = "GRENADE";

    /// <summary>
    ///     Keyed on (owner, tag), because <c>Bomb_Type</c> is declared on three ability types and
    ///     only <c>RemoteBombAbility</c> asserts this. Attribution follows the xref to the assert,
    ///     never where the schema happens to declare the tag.
    /// </summary>
    private static readonly (string Owner, string Tag)[] Owners =
    [
        ("Grenade_Attack_Ability", "Grenade_Type"),
        ("Remote_Bomb_Ability", "Bomb_Type"),
    ];

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.GrenadeProjectileCategory;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.OwningType is not { } owner) return [];
        if (!Owners.Any(o =>
                string.Equals(o.Owner, owner, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(o.Tag, fact.Tag.Tag, StringComparison.OrdinalIgnoreCase)))
            return [];

        // No resolver means the question cannot be answered here, and "cannot tell" must not read
        // as "fails" - the same rule every cross-object handler follows.
        if (ctx.Objects is null) return [];

        var named = fact.RawValue.Trim();
        if (named.Length == 0) return [];

        var resolved = ctx.Objects.Resolve(named);

        // An id nothing defines is the unresolved-reference check's business. Reporting it again
        // here would put two diagnostics on one typo and name the wrong cause.
        if (!resolved.Found) return [];

        // The EFFECTIVE object, so a category inherited through Variant_Of_Existing_Type counts -
        // four of the ten shipped declarations name a variant that inherits it, and reading the
        // node instead would report every one of them.
        var category = resolved.Tags
            .LastOrDefault(t => string.Equals(t.TagName, CategoryTag, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();

        // A target with no category at all cannot be judged. The engine has a default here and it
        // was not measured, so calling it "not a grenade" would be inventing the answer.
        if (string.IsNullOrEmpty(category)) return [];

        if (string.Equals(category, RequiredCategory, StringComparison.OrdinalIgnoreCase)) return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"<{fact.Tag.Tag}> names '{named}', whose <{CategoryTag}> is {category} - this "
                + $"ability only accepts a {RequiredCategory} projectile and refuses anything else")
        ];
    }
}
