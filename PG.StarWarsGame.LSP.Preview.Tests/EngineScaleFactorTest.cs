// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Preview;

namespace PG.StarWarsGame.LSP.Preview.Tests;

/// <summary>
///     The render scale a <c>Scale_Factor</c> value means: the number the game reads from it, and
///     the identity wherever that number could not size an object.
/// </summary>
public sealed class EngineScaleFactorTest
{
    [Theory]
    [InlineData("2.0", 2f)]
    [InlineData(" 0.75 ", 0.75f)]
    [InlineData(".85", 0.85f)]
    // The suffix the files write on other floats: the game reads the number in front of it.
    [InlineData("2.5f", 2.5f)]
    [InlineData(" 2.5f ", 2.5f)]
    [InlineData("1.5ff", 1.5f)]
    public void Of_reads_the_number_the_game_reads(string written, float expected)
    {
        Assert.Equal(expected, EngineScaleFactor.Of(written));
    }

    /// <summary>A zero or negative scale collapses the object, so the engine falls back to 1.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("-1")]
    [InlineData("-0.5f")]
    public void Of_is_the_identity_for_a_scale_that_cannot_size_an_object(string written)
    {
        Assert.Equal(EngineScaleFactor.None, EngineScaleFactor.Of(written));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Of_is_the_identity_when_no_number_is_written(string? written)
    {
        Assert.Equal(EngineScaleFactor.None, EngineScaleFactor.Of(written));
    }
}