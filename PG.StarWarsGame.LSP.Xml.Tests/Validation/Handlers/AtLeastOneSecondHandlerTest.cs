// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     A duration the engine refuses to run shorter than one second.
/// </summary>
/// <remarks>
///     <para>
///         <c>LeechShieldsAbilityClass::Validate_Data</c> (<c>01028000</c>) tests
///         <c>Duration &lt;= 1.0 &amp;&amp; Duration != 1.0</c> - which is <c>Duration &lt; 1.0</c> -
///         then sets <c>Duration = 1.0</c>. So one second is legal and the bound is inclusive,
///         which the message ("should have at least one second of time") happens to state
///         correctly.
///     </para>
///     <para>
///         Opted into per owner: <c>Duration_In_Secs</c> also appears on
///         <c>Sensor_Jamming_Ability</c> and <c>System_Spy_Ability</c>, whose validators say
///         nothing about it. The one shipped Leech_Shields object, the Kadalbe battleship's, sets a
///         duration well above the bound.
///     </para>
/// </remarks>
public sealed class AtLeastOneSecondHandlerTest
{
    private static readonly AtLeastOneSecondHandler Handler = new();

    private static readonly XmlTagDefinition Duration =
        XmlHandlerTestFixtures.MakeTag("Duration_In_Secs", XmlValueType.Float);

    [Fact]
    public void ValidationId_is_stable()
    {
        // The schema refers to this by name; renaming it silently disables every tag using it.
        Assert.Equal("at-least-one-second", Handler.ValidationId);
    }

    // The bound is INCLUSIVE - the engine's repair value is 1.0 itself.
    [Theory]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1.5")]
    [InlineData("30")]
    public void One_second_and_above_is_accepted(string value)
    {
        Assert.Empty(Handler.Handle(XmlHandlerTestFixtures.MakeFact(Duration, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.9")]
    [InlineData("-4")]
    public void Below_one_second_is_reported(string value)
    {
        var d = Assert.Single(Handler.Handle(XmlHandlerTestFixtures.MakeFact(Duration, value),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Contains("Duration_In_Secs", d.Message, StringComparison.Ordinal);
    }

    // A value that is not a number belongs to the type handler.
    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    public void A_non_numeric_value_is_left_to_the_type_handler(string value)
    {
        Assert.Empty(Handler.Handle(XmlHandlerTestFixtures.MakeFact(Duration, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }
}