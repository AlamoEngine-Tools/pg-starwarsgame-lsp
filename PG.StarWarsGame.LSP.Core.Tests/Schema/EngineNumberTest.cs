// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Tests.Schema;

/// <summary>
///     The two readings of a number in the files: the well-formed number a validator asks for, and
///     the number the game reads, which stops at the first character that is not part of one.
/// </summary>
public sealed class EngineNumberTest
{
    [Theory]
    [InlineData("1.5", 1.5f)]
    [InlineData("1.5f", 1.5f)]
    [InlineData(" 2F ", 2f)]
    [InlineData(".5", 0.5f)]
    [InlineData("-40.0", -40f)]
    [InlineData("1e3", 1000f)]
    [InlineData("+3", 3f)]
    public void TryParseFloat_reads_a_number_with_at_most_one_suffix(string text, float expected)
    {
        Assert.True(EngineNumber.TryParseFloat(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("1.5ff")]
    [InlineData("1.5 f")]
    [InlineData("1,5")]
    [InlineData("1.5x")]
    [InlineData("f")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParseFloat_refuses_anything_beyond_the_number_and_its_suffix(string? text)
    {
        Assert.False(EngineNumber.TryParseFloat(text, out var value));
        Assert.Equal(0f, value);
    }

    [Theory]
    [InlineData("0.5f", 0.5)]
    [InlineData("1100.0F", 1100.0)]
    [InlineData("-1", -1.0)]
    public void TryParseDouble_follows_the_same_rule(string text, double expected)
    {
        Assert.True(EngineNumber.TryParseDouble(text, out var value));
        Assert.Equal(expected, value);
        Assert.False(EngineNumber.TryParseDouble(text + "ff", out _));
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData(" -7 ", -7)]
    [InlineData("+12", 12)]
    public void TryParseInt_reads_a_whole_number(string text, int expected)
    {
        Assert.True(EngineNumber.TryParseInt(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("3.0")]
    [InlineData("3f")]
    [InlineData("1e3")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseInt_refuses_a_fraction_a_suffix_and_an_exponent(string? text)
    {
        Assert.False(EngineNumber.TryParseInt(text, out _));
    }

    /// <summary>The game converts with atof: leading blanks skipped, the number taken, the rest ignored.</summary>
    [Theory]
    [InlineData("1.5", 1.5f)]
    [InlineData("1.5f", 1.5f)]
    [InlineData("1.5ff", 1.5f)]
    [InlineData("1.5 f", 1.5f)]
    [InlineData(" 2 ", 2f)]
    [InlineData("1,5", 1f)]
    [InlineData("-.5x", -0.5f)]
    [InlineData("1e3f", 1000f)]
    [InlineData("1e", 1f)]
    [InlineData("1.", 1f)]
    [InlineData("+3", 3f)]
    [InlineData("7abc", 7f)]
    public void ReadFloat_takes_the_number_the_game_takes(string text, float expected)
    {
        Assert.Equal(expected, EngineNumber.ReadFloat(text));
    }

    /// <summary>No number at all: the game reads 0 there, which the caller must decide how to treat.</summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("e5")]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData("f1.5")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ReadFloat_is_null_when_no_number_starts_the_text(string? text)
    {
        Assert.Null(EngineNumber.ReadFloat(text));
    }

    [Fact]
    public void ReadDouble_follows_the_same_rule()
    {
        Assert.Equal(0.5, EngineNumber.ReadDouble("0.5ff"));
        Assert.Equal(1100.0, EngineNumber.ReadDouble("1100.0f"));
        Assert.Null(EngineNumber.ReadDouble("none"));
    }

    /// <summary>The game converts with atoi: the sign and the digits at the front, the rest ignored.</summary>
    [Theory]
    [InlineData("3", 3)]
    [InlineData(" -7 ", -7)]
    [InlineData("+12", 12)]
    [InlineData("1.0", 1)]
    [InlineData("2.9", 2)]
    [InlineData("3f", 3)]
    [InlineData("1e3", 1)]
    [InlineData("7abc", 7)]
    [InlineData("0x10", 0)]
    public void ReadInt_takes_the_whole_number_the_game_takes(string text, int expected)
    {
        Assert.Equal(expected, EngineNumber.ReadInt(text));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData(".5")]
    [InlineData("-")]
    [InlineData("f3")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ReadInt_is_null_when_no_digit_starts_the_text(string? text)
    {
        Assert.Null(EngineNumber.ReadInt(text));
    }
}