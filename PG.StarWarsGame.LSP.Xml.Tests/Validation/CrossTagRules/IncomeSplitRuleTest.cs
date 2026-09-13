// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.CrossTagRules;

/// <summary>
///     The three rules in <c>IncomeStreamAbilityClass::Validate_Data</c> (<c>0100f6a0</c>).
/// </summary>
/// <remarks>
///     <para>
///         All three are conditional, which is why none of them is a range on a tag: the percentage
///         is only checked while the income is actually being split, and the two flag rules only
///         fire when <c>Split_Favors_Owner</c> is on.
///     </para>
///     <para>
///         The engine's own message for the second is <strong>backwards</strong> - it says
///         "irrelevant when Split_Income_With_Allies is true" and the branch is the else, so it
///         fires when allies splitting is OFF. The code is the specification.
///     </para>
///     <para>
///         Measured: 45 shipped <c>Income_Stream_Ability</c> objects, 2 with
///         <c>Split_Favors_Owner</c> on, and none that any of the three rules would report.
///     </para>
/// </remarks>
public sealed class IncomeSplitRuleTest
{
    private const string Uri = "file:///abilities/Abilities.xml";

    /// <summary>
    ///     The engine clamps to 0.99 at the top, NOT to 1.0 - so the fix writes the value it
    ///     actually picks.
    /// </summary>
    [Theory]
    [InlineData("1.5", "0.99")]
    [InlineData("1.0", "0.99")]
    [InlineData("-0.25", "0.0")]
    public void An_out_of_range_share_is_reported_with_the_engines_clamp(string value, string clamped)
    {
        var d = Assert.Single(Diagnose(Splitting(value), new OwnerIncomeShareRule(),
            new OwnerIncomeShareHandler()));

        Assert.Equal(clamped, d.SuggestedFix);
        Assert.Contains("engine", d.FixTitle!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Zero passes, whatever the message says about being greater than zero.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("0.5")]
    [InlineData("0.99")]
    public void A_share_inside_the_range_is_silent(string value)
    {
        Assert.Empty(Facts(Splitting(value), new OwnerIncomeShareRule()));
    }

    /// <summary>
    ///     The range is only checked when the income is being split and the split favours the owner.
    ///     Enforcing it unconditionally would flag a value the engine never reads.
    /// </summary>
    [Theory]
    [InlineData("No", "Yes")]
    [InlineData("Yes", "No")]
    [InlineData("No", "No")]
    public void The_share_is_unchecked_unless_both_flags_are_on(string favors, string allies)
    {
        var body = "<Income_Stream_Ability Name='I'>"
                   + $"<Split_Favors_Owner>{favors}</Split_Favors_Owner>"
                   + $"<Split_Income_With_Allies>{allies}</Split_Income_With_Allies>"
                   + "<Owner_Income_Percentage>5.0</Owner_Income_Percentage>"
                   + "</Income_Stream_Ability>";

        Assert.Empty(Facts(body, new OwnerIncomeShareRule()));
    }

    /// <summary>
    ///     <c>Split_Favors_Owner</c> without an allies split does nothing, and the engine turns it
    ///     off and zeroes the share.
    /// </summary>
    [Fact]
    public void A_favoured_split_with_no_allies_split_is_reported()
    {
        var body = "<Income_Stream_Ability Name='I'>\n"
                   + "    <Split_Favors_Owner>Yes</Split_Favors_Owner>\n"
                   + "    <Owner_Income_Percentage>0.5</Owner_Income_Percentage>\n"
                   + "</Income_Stream_Ability>";

        var fact = Assert.Single(Facts(body, new SplitFavorsOwnerIgnoredRule())
            .OfType<IncomeSplitConflictFact>());

        Assert.Equal("Split_Favors_Owner", fact.FlagTag);

        // The engine clears the flag AND zeroes the share, so the repair does both.
        Assert.Equal(2, fact.Repair!.Edits.Count);
        Assert.Equal("No", fact.Repair.Edits[0].NewText);
        Assert.Equal("0.0", fact.Repair.Edits[1].NewText);
    }

    /// <summary>With no share tag written there is only one thing to clear.</summary>
    [Fact]
    public void The_repair_only_touches_tags_that_are_there()
    {
        var body = "<Income_Stream_Ability Name='I'>"
                   + "<Split_Favors_Owner>Yes</Split_Favors_Owner></Income_Stream_Ability>";

        var fact = Assert.Single(Facts(body, new SplitFavorsOwnerIgnoredRule())
            .OfType<IncomeSplitConflictFact>());

        Assert.Equal("No", Assert.Single(fact.Repair!.Edits).NewText);
    }

    [Fact]
    public void An_allies_split_makes_the_favour_flag_meaningful()
    {
        Assert.Empty(Facts(Splitting("0.5"), new SplitFavorsOwnerIgnoredRule()));
    }

    /// <summary>The two flags the engine refuses to have on together - it clears both.</summary>
    [Fact]
    public void Favouring_the_owner_while_paying_everyone_in_full_is_reported()
    {
        var body = "<Income_Stream_Ability Name='I'>"
                   + "<Split_Favors_Owner>Yes</Split_Favors_Owner>"
                   + "<Split_Income_With_Allies>Yes</Split_Income_With_Allies>"
                   + "<Full_Amount_To_Everyone>Yes</Full_Amount_To_Everyone>"
                   + "</Income_Stream_Ability>";

        var fact = Assert.Single(Facts(body, new SplitFavorsOwnerVsFullAmountRule())
            .OfType<IncomeSplitConflictFact>());

        Assert.Equal("Full_Amount_To_Everyone", fact.OtherTag);
        Assert.Equal(2, fact.Repair!.Edits.Count);
    }

    /// <summary>
    ///     Ordering, and it is measured rather than assumed: the ignored-flag rule runs FIRST and
    ///     clears <c>Split_Favors_Owner</c>, so by the time the engine tests it against
    ///     <c>Full_Amount_To_Everyone</c> the flag is already off and it never complains. Reporting
    ///     both would put two diagnostics where the game emits one.
    /// </summary>
    [Fact]
    public void The_conflict_is_not_reported_when_the_flag_was_already_disarmed()
    {
        var body = "<Income_Stream_Ability Name='I'>"
                   + "<Split_Favors_Owner>Yes</Split_Favors_Owner>"
                   + "<Full_Amount_To_Everyone>Yes</Full_Amount_To_Everyone>"
                   + "</Income_Stream_Ability>";

        Assert.Empty(Facts(body, new SplitFavorsOwnerVsFullAmountRule()));
    }

    private static string Splitting(string percentage)
    {
        return "<Income_Stream_Ability Name='I'>"
               + "<Split_Favors_Owner>Yes</Split_Favors_Owner>"
               + "<Split_Income_With_Allies>Yes</Split_Income_With_Allies>"
               + $"<Owner_Income_Percentage>{percentage}</Owner_Income_Percentage>"
               + "</Income_Stream_Ability>";
    }

    private static IReadOnlyList<XmlDiagnosticResult> Diagnose(
        string body, IXmlCrossTagRule rule, OwnerIncomeShareHandler handler)
    {
        return Facts(body, rule)
            .OfType<OwnerIncomeShareFact>()
            .SelectMany(f => handler.Handle(f, XmlHandlerTestFixtures.EmptyCtx))
            .ToList();
    }

    private static IReadOnlyList<XmlFact> Facts(string body, IXmlCrossTagRule rule)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            [rule]);

        return producer.Produce("<Root>\n" + body + "\n</Root>", Uri);
    }
}

file sealed class EmptyFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string fileUri)
    {
    }
}
