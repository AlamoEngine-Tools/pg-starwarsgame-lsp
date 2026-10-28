// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Tests.Schema;

/// <summary>
///     The values the game's object parser maps nothing from: blank, and exactly TBD.
/// </summary>
public sealed class EngineTagValueTest
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    [InlineData("TBD")]
    [InlineData(" TBD ")]
    [InlineData(null)]
    public void IsWithheld_for_a_blank_and_for_the_placeholder(string? value)
    {
        Assert.True(EngineTagValue.IsWithheld(value));
    }

    // The parser compares the placeholder byte for byte: a different case is a value like any other.
    [Theory]
    [InlineData("tbd")]
    [InlineData("TBD.")]
    [InlineData("0")]
    [InlineData("i_button_han_solo.tga")]
    public void IsWithheld_is_false_for_a_value_the_game_maps(string value)
    {
        Assert.False(EngineTagValue.IsWithheld(value));
    }

    [Theory]
    [InlineData("<Icon_Name></Icon_Name>")]
    [InlineData("<Icon_Name />")]
    [InlineData("<Icon_Name>TBD</Icon_Name>")]
    [InlineData("<Icon_Name><!-- cleared --></Icon_Name>")]
    public void WritesNothing_for_a_plain_tag_with_a_withheld_value(string fragment)
    {
        Assert.True(EngineTagValue.WritesNothing("", fragment));
    }

    /// <summary>A sub-object list has no text value to map; the engine reads it another way.</summary>
    [Fact]
    public void WritesNothing_is_false_for_a_tag_with_element_children()
    {
        const string fragment =
            "<Unit_Abilities_Data><Unit_Ability><Type>DEFEND</Type></Unit_Ability></Unit_Abilities_Data>";

        Assert.False(EngineTagValue.WritesNothing("", fragment));
    }

    [Fact]
    public void WritesNothing_is_false_for_a_value_the_game_maps()
    {
        Assert.False(EngineTagValue.WritesNothing("i_button_han_solo.tga",
            "<Icon_Name>i_button_han_solo.tga</Icon_Name>"));
    }
}