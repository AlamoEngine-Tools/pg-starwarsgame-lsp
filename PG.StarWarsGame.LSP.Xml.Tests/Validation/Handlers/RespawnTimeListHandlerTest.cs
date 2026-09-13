// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     The per-tech-level respawn table, as <c>Validate_Respawn_Times</c> checks it.
/// </summary>
/// <remarks>
///     <para>
///         One engine function, three checks, shared by <c>SlicerAbilityClass</c> and
///         <c>BlackMarketAbilityClass</c>: exactly five entries, none below zero, and zeros
///         all-or-nothing. It returns false on the FIRST failure and the caller then CLEARS both
///         lists, so a single bad entry costs the whole table.
///     </para>
///     <para>
///         Measured: all six shipped tables - R2D2's and the three Black Market heroes' - are five
///         non-negative entries with no zeros, so the rule is silent on vanilla while the count
///         check is genuinely exercised by it.
///     </para>
/// </remarks>
public sealed class RespawnTimeListHandlerTest
{
    private static readonly RespawnTimeListHandler Handler = new();

    [Fact]
    public void ValidationId_is_stable()
    {
        Assert.Equal("respawn-time-list", Handler.ValidationId);
    }

    [Theory]
    [InlineData("60.0, 60.0, 60.0, 70.0, 70.0")]
    [InlineData("30, 30, 30, 30, 30")]
    // All zero is explicitly legal - the engine's own message says so.
    [InlineData("0, 0, 0, 0, 0")]
    public void A_valid_table_is_silent(string value)
    {
        Assert.Empty(Report(value));
    }

    [Theory]
    [InlineData("60.0, 60.0, 60.0")]
    [InlineData("60, 60, 60, 60, 60, 60")]
    public void A_table_that_is_not_five_entries_is_reported(string value)
    {
        Assert.Contains("tech level", Assert.Single(Report(value)).Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_negative_entry_is_reported()
    {
        Assert.Contains("0 or greater", Assert.Single(Report("60, 60, -1, 60, 60")).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     The latch in the engine: a zero anywhere means every entry must be zero, whichever side
    ///     of the list it sits on.
    /// </summary>
    [Theory]
    [InlineData("0, 60, 60, 60, 60")]
    [InlineData("60, 60, 60, 60, 0")]
    [InlineData("60, 0, 0, 0, 0")]
    public void A_zero_among_non_zeros_is_reported(string value)
    {
        Assert.Contains("every entry", Assert.Single(Report(value)).Message,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The engine stops at the first failure, and so does this - a wrong-length list full of
    ///     negatives is one problem to fix, not three.
    /// </summary>
    [Fact]
    public void Only_the_first_failure_is_reported()
    {
        Assert.Single(Report("-1, -1"));
    }

    /// <summary>
    ///     A ragged or mistyped list belongs to the FloatList type handler; one mistake should not
    ///     collect two diagnostics that say different things.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("60, sixty, 60, 60, 60")]
    public void A_non_numeric_list_is_left_to_the_type_handler(string value)
    {
        Assert.Empty(Report(value));
    }

    /// <summary>
    ///     No quick fix. The engine's repair is to CLEAR both lists, and putting the deletion of an
    ///     author's respawn table one keystroke away is not a service.
    /// </summary>
    [Fact]
    public void No_fix_is_offered()
    {
        var d = Assert.Single(Report("60, 60, 60"));

        Assert.Null(d.SuggestedFix);
        Assert.Null(d.EngineRepair);
    }

    private static IReadOnlyList<XmlDiagnosticResult> Report(string value)
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Min_Respawn_Times", XmlValueType.FloatList);
        var fact = new XmlTagValueFact("file:///test.xml", 0, 0, value.Length, tag, value,
            "BlackMarketAbility");

        return Handler.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
    }
}
