// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     <c>allowedValues</c> narrows a tag for ONE owning type, whatever the tag's value type.
/// </summary>
/// <remarks>
///     <para>
///         It began life inside <see cref="DynamicEnumValueHandler" />, which was wrong the moment
///         a second value type needed it: <c>Causes_Despawn</c> is a Boolean, and
///         <c>GalacticSabotageAbilityClass::Validate_Data</c> (<c>00ef6c30</c>) demands it be Yes
///         and sets it itself otherwise. A restriction that only worked on enums would have
///         silently done nothing there.
///     </para>
///     <para>
///         The comparison follows the tag's type. For a Boolean the engine reads Yes, True and 1
///         as the same value, so an author who wrote <c>True</c> where the schema says <c>Yes</c>
///         has written the right thing.
///     </para>
/// </remarks>
public sealed class AllowedValuesHandlerTest
{
    private static readonly AllowedValuesHandler Sut = new();

    // ── booleans compare by meaning ──────────────────────────────────────────

    [Theory]
    [InlineData("Yes")]
    [InlineData("True")]
    [InlineData("1")]
    public void Boolean_spellings_of_the_allowed_value_pass(string spelling)
    {
        Assert.Empty(Run(Boolean("Causes_Despawn", "Yes"), spelling));
    }

    [Theory]
    [InlineData("No")]
    [InlineData("False")]
    [InlineData("0")]
    public void Boolean_spellings_of_the_other_value_are_reported(string spelling)
    {
        var result = Assert.Single(Run(Boolean("Causes_Despawn", "Yes"), spelling));

        Assert.Equal(XmlDiagnosticSeverity.Error, result.Severity);
        Assert.Contains("Yes", result.Message);
    }

    // A value that is not a boolean at all is the type handler's business, not this one's - one
    // typo must not collect two diagnostics.
    [Fact]
    public void Boolean_garbage_is_left_to_the_type_handler()
    {
        Assert.Empty(Run(Boolean("Causes_Despawn", "Yes"), "Maybe"));
    }

    // ── everything else compares as text ─────────────────────────────────────

    [Fact]
    public void Enum_value_outside_the_subset_is_reported()
    {
        Assert.Single(Run(Enum("Activation_Style", "Galactic_Automatic"), "Ground_Automatic"));
    }

    [Theory]
    [InlineData("Galactic_Automatic")]
    [InlineData("GALACTIC_AUTOMATIC")]
    public void Enum_value_inside_the_subset_passes(string value)
    {
        Assert.Empty(Run(Enum("Activation_Style", "Galactic_Automatic"), value));
    }

    // Every tag that states no subset, which is nearly all of them.
    [Fact]
    public void A_tag_with_no_subset_is_untouched()
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Max_Speed", XmlValueType.Float);

        Assert.Empty(Run(tag, "anything at all"));
    }

    private static IReadOnlyList<XmlDiagnosticResult> Run(XmlTagDefinition tag, string value)
    {
        return Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
    }

    private static XmlTagDefinition Boolean(string name, params string[] allowed)
    {
        return XmlHandlerTestFixtures.MakeTag(name, XmlValueType.Boolean) with { AllowedValues = allowed };
    }

    private static XmlTagDefinition Enum(string name, params string[] allowed)
    {
        return XmlHandlerTestFixtures.MakeTag(name, XmlValueType.DynamicEnumValue) with
        {
            AllowedValues = allowed
        };
    }
}
