// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Tests.Schema;

/// <summary>
///     The engine's boolean spellings, and the two different defaults a missing tag can carry.
/// </summary>
public sealed class EngineBooleanTest
{
    [Theory]
    [InlineData("Yes")]
    [InlineData("true")]
    [InlineData(" 1 ")]
    public void AffirmativeSpellings_read_as_true(string value)
    {
        Assert.True(EngineBoolean.IsTrue(value));
        Assert.True(EngineBoolean.IsTrueUnlessDenied(value));
    }

    [Theory]
    [InlineData("No")]
    [InlineData("false")]
    [InlineData("0")]
    public void NegativeSpellings_read_as_false_either_way(string value)
    {
        Assert.False(EngineBoolean.IsTrue(value));
        Assert.False(EngineBoolean.IsTrueUnlessDenied(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnAbsentTag_is_off_by_default_and_ON_for_a_default_on_flag(string? value)
    {
        // The whole point of the second reading. A flag the corpus never writes down is governed
        // entirely by its default, so which default it takes IS the behaviour.
        Assert.False(EngineBoolean.IsTrue(value));
        Assert.True(EngineBoolean.IsTrueUnlessDenied(value));
    }

    [Fact]
    public void AnUnrecognisedSpelling_is_not_a_denial()
    {
        // It is not "No", so a default-on flag stays on. Whether the value is a spelling at all is
        // a validation question, answered by IsValid, and answering it here would silently turn a
        // feature off over a typo.
        Assert.True(EngineBoolean.IsTrueUnlessDenied("maybe"));
        Assert.False(EngineBoolean.IsValid("maybe"));
    }
}
