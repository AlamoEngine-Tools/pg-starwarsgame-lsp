// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     Filling an ability row's name, description and icon.
/// </summary>
/// <remarks>
///     The precedence is the same for all three and it comes from the data: an <c>Alternate_*</c> tag
///     overrides per INSTANCE, otherwise the engine's own convention applies. Measured 2026-08-27:
///     every one of the 70 shipped ability types has both convention keys, so a missing name means a
///     mod removed it rather than a gap in our table.
/// </remarks>
public sealed class PreviewAbilityTextTest
{
    private static readonly byte[] SomeIcon = [1];

    private sealed class FakeLocalisation(params (string Key, string Value)[] rows) : ILocalisationIndex
    {
        private readonly Dictionary<string, string> _rows =
            rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> Keys => _rows.Keys;

        public bool ContainsKey(string key) => _rows.ContainsKey(key);

        public string? GetValue(string key) => _rows.GetValueOrDefault(key);
    }

    private static IconCatalog CatalogWith(params string[] names)
    {
        return new IconCatalog(
            names.ToDictionary(n => n, _ => SomeIcon, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, byte[]>(),
            new Dictionary<string, byte[]>());
    }

    private static PreviewAbility Ability(string type) =>
        new(type, null, null, null, null, null, [], null, null, []);

    // ── names ────────────────────────────────────────────────────────────────

    [Fact]
    public void Name_ComesFromTheConventionKey()
    {
        var loca = new FakeLocalisation(("TEXT_TOOLTIP_ABILITY_DEFEND_NAME", "Boost Shield Power"));

        var filled = PreviewAbilityText.Fill(Ability("DEFEND"), null, null, null, loca, []);

        Assert.Equal("Boost Shield Power", filled.Name);
    }

    [Fact]
    public void AlternateNameText_OverridesTheConvention()
    {
        var loca = new FakeLocalisation(
            ("TEXT_TOOLTIP_ABILITY_DEFEND_NAME", "Boost Shield Power"),
            ("TEXT_TOOLTIP_BOSSK_SWAP", "Swap Weapon"));

        var filled = PreviewAbilityText.Fill(
            Ability("DEFEND"), "TEXT_TOOLTIP_BOSSK_SWAP", null, null, loca, []);

        Assert.Equal("Swap Weapon", filled.Name);
    }

    // A mod that overrides the text and does not ship the key leaves the row with no name. Falling
    // back to the convention would show the STOCK text for an ability the mod has redefined, which
    // is worse than showing none.
    [Fact]
    public void AlternateNameTextThatDoesNotResolve_LeavesTheNameEmpty()
    {
        var loca = new FakeLocalisation(("TEXT_TOOLTIP_ABILITY_DEFEND_NAME", "Boost Shield Power"));

        var filled = PreviewAbilityText.Fill(
            Ability("DEFEND"), "TEXT_MOD_FORGOT_THIS", null, null, loca, []);

        Assert.Null(filled.Name);
    }

    [Fact]
    public void Description_ComesFromTheConventionKey()
    {
        var loca = new FakeLocalisation(
            ("TEXT_TOOLTIP_ABILITY_DEFEND_DESCRIPTION", "Boost shield regeneration."));

        var filled = PreviewAbilityText.Fill(Ability("DEFEND"), null, null, null, loca, []);

        Assert.Equal("Boost shield regeneration.", filled.Description);
    }

    [Fact]
    public void NoLocalisationAtAll_LeavesTheRowUnnamedRatherThanThrowing()
    {
        var filled = PreviewAbilityText.Fill(Ability("DEFEND"), null, null, null, null, []);

        Assert.Null(filled.Name);
        Assert.Null(filled.Description);
    }

    // ── icons ────────────────────────────────────────────────────────────────

    [Fact]
    public void Icon_UsesTheSharedResolver()
    {
        var filled = PreviewAbilityText.Fill(
            Ability("ROCKET_ATTACK"), null, null, CatalogWith("I_SA_ROCKET_ATTACK.TGA"), null, []);

        Assert.NotNull(filled.IconDataUri);
        Assert.StartsWith("data:image/png;base64,", filled.IconDataUri);
    }

    // Ambiguous, so nothing is asserted: no icon and, importantly, no problem either.
    [Fact]
    public void UnknownIcon_YieldsNeitherAnIconNorAProblem()
    {
        var problems = new List<PreviewProblem>();

        var filled = PreviewAbilityText.Fill(
            Ability("SUPER_LASER"), null, null, CatalogWith("I_SA_ROCKET_ATTACK.TGA"), null, problems);

        Assert.Null(filled.IconDataUri);
        Assert.Empty(problems);
    }

    // A declared icon that is not there IS reportable - the ability names art the mod does not ship,
    // and the game would draw the missing-art marker. The row still gets the placeholder so the
    // layout holds.
    [Fact]
    public void DeclaredButMissingIcon_DrawsThePlaceholderAndReportsIt()
    {
        var problems = new List<PreviewProblem>();

        var filled = PreviewAbilityText.Fill(
            Ability("ROCKET_ATTACK"), null, "I_SA_NOT_DRAWN.TGA",
            CatalogWith("I_SA_ROCKET_ATTACK.TGA"), null, problems);

        Assert.NotNull(filled.IconDataUri);
        var problem = Assert.Single(problems);
        Assert.Contains("I_SA_NOT_DRAWN.TGA", problem.Message);
    }

    [Fact]
    public void NoCatalog_LeavesTheIconEmpty()
    {
        var filled = PreviewAbilityText.Fill(Ability("ROCKET_ATTACK"), null, null, null, null, []);

        Assert.Null(filled.IconDataUri);
    }
}
