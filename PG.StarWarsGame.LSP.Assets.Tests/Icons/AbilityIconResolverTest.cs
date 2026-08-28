// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

/// <summary>
///     The rules for which command-bar icon an ability draws.
/// </summary>
/// <remarks>
///     These lived as a private method inside the encyclopedia handler until the model preview needed
///     the same answer. Two copies of "which icon does this ability draw" is two places for the
///     Alternate_* precedence and the ambiguity rule to drift apart, which is exactly the class of
///     bug that had the editor and the preview disagreeing about whether an icon existed at all.
/// </remarks>
public sealed class AbilityIconResolverTest
{
    private static readonly byte[] SomeIcon = [1];

    private static IconCatalog CatalogWith(params string[] names)
    {
        return new IconCatalog(
            names.ToDictionary(n => n, _ => SomeIcon, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, byte[]>(),
            new Dictionary<string, byte[]>());
    }

    // ── the Alternate_Icon_Name override ─────────────────────────────────────

    // An Alternate_* tag REPLACES the default it shadows, per instance rather than per type: two
    // units can give the same ability type different icons this way.
    [Fact]
    public void DeclaredAlternateIcon_WinsOverTheTypeConvention()
    {
        var catalog = CatalogWith("I_SA_MINE.TGA", "I_SA_ROCKET_ATTACK.TGA");

        var result = AbilityIconResolver.Resolve(catalog, "ROCKET_ATTACK", "I_SA_MINE.TGA");

        Assert.Equal(AbilityIconOutcome.Resolved, result.Outcome);
        Assert.NotNull(result.Icon);
    }

    // A declared override that does not resolve draws the missing-icon placeholder, because that is
    // what the engine itself draws. Substituting the default would hide a broken reference behind a
    // plausible icon the game would never show.
    [Fact]
    public void DeclaredAlternateIconThatIsMissing_ReportsTheMissingPlaceholder()
    {
        var catalog = CatalogWith("I_SA_ROCKET_ATTACK.TGA");

        var result = AbilityIconResolver.Resolve(catalog, "ROCKET_ATTACK", "I_SA_NOT_DRAWN.TGA");

        Assert.Equal(AbilityIconOutcome.DeclaredButMissing, result.Outcome);
        Assert.Null(result.Icon);
    }

    // ── the confirmed exceptions ─────────────────────────────────────────────

    // Several icons cannot be guessed from the type at all - INVULNERABILITY draws EVASIVE_MANEUVERS
    // - so the recorded table has to beat the naming convention.
    [Fact]
    public void MappedException_BeatsTheTypeConvention()
    {
        var catalog = CatalogWith("I_SA_EVASIVE_MANEUVERS.TGA", "I_SA_INVULNERABILITY.TGA");

        var result = AbilityIconResolver.Resolve(catalog, "INVULNERABILITY", null);

        Assert.Equal(AbilityIconOutcome.Resolved, result.Outcome);
        Assert.Same(AbilityIconNames.For("INVULNERABILITY"), AbilityIconNames.For("INVULNERABILITY"));
    }

    // ── the I_SA_<TYPE> convention ───────────────────────────────────────────

    [Fact]
    public void UnmappedType_FallsBackToTheNamingConvention()
    {
        var catalog = CatalogWith("I_SA_ROCKET_ATTACK.TGA");

        var result = AbilityIconResolver.Resolve(catalog, "ROCKET_ATTACK", null);

        Assert.Equal(AbilityIconOutcome.Resolved, result.Outcome);
        Assert.NotNull(result.Icon);
    }

    [Fact]
    public void TypeIsMatchedWithoutRegardToCase()
    {
        var catalog = CatalogWith("I_SA_ROCKET_ATTACK.TGA");

        var result = AbilityIconResolver.Resolve(catalog, "rocket_attack", null);

        Assert.Equal(AbilityIconOutcome.Resolved, result.Outcome);
    }

    // A miss HERE is AMBIGUOUS and must draw NEITHER the icon nor the placeholder: I_SA_<TYPE> is
    // only a guess at the engine's hardcoded name, so a miss can equally mean the real name is
    // something else entirely while the game shows perfectly good art.
    [Fact]
    public void UnknownType_ReportsNeitherIconNorPlaceholder()
    {
        var catalog = CatalogWith("I_SA_ROCKET_ATTACK.TGA");

        var result = AbilityIconResolver.Resolve(catalog, "SUPER_LASER", null);

        Assert.Equal(AbilityIconOutcome.Unknown, result.Outcome);
        Assert.Null(result.Icon);
    }

    // ── degenerate input ─────────────────────────────────────────────────────

    [Fact]
    public void NoCatalog_ReportsUnknown()
    {
        var result = AbilityIconResolver.Resolve(null, "ROCKET_ATTACK", null);

        Assert.Equal(AbilityIconOutcome.Unknown, result.Outcome);
        Assert.Null(result.Icon);
    }

    [Fact]
    public void BlankAlternateIcon_IsTreatedAsAbsentRatherThanBroken()
    {
        var catalog = CatalogWith("I_SA_ROCKET_ATTACK.TGA");

        var result = AbilityIconResolver.Resolve(catalog, "ROCKET_ATTACK", "   ");

        Assert.Equal(AbilityIconOutcome.Resolved, result.Outcome);
    }
}
