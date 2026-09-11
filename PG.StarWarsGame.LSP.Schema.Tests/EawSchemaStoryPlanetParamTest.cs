// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     The planet parameter on the <c>StoryEventEnterClass</c> family is OPTIONAL, and omitting it
///     means "any planet" rather than being an error (issue #125).
/// </summary>
/// <remarks>
///     <para>
///         The engine says so outright. All five events below are constructed as
///         <c>StoryEventEnterClass</c> (<c>StoryEvent.cpp</c>, one shared case block), and its
///         <c>Evaluate_Event</c> opens with:
///     </para>
///     <code>
///         // If no planet is specified in the script, assume any planet will do
///         if (Planet.empty()) { ... Event_Triggered(planet,false); return; }
///     </code>
///     <para>
///         Our own text already documented that - <c>STORY_ENTER</c>'s description reads "If no
///         planet is specified, all parameters are ignored except the stealth flag" - while the
///         parameter was still declared required, so the schema contradicted itself and the
///         required-param warning fired on correct data. Measured against the shipped corpus, this
///         is the whole of the problem: of 64 declared events, exactly two omit a required param in
///         vanilla (<c>STORY_PLANET_DESTROYED</c> in 2 of 2 uses, <c>STORY_FLEET_BOUNCED</c> in 8
///         of 59), and both are in this family.
///     </para>
/// </remarks>
public sealed class EawSchemaStoryPlanetParamTest
{
    [Theory]
    [InlineData("STORY_ENTER")]
    [InlineData("STORY_LAND_ON")]
    [InlineData("STORY_PLANET_DESTROYED")]
    [InlineData("STORY_FLEET_BOUNCED")]
    [InlineData("STORY_INVASION_BOUNCED")]
    public void PlanetParam_IsOptional_BecauseOmittingItMeansAnyPlanet(string eventName)
    {
        var value = StoryEventSchema.Value(eventName);

        Assert.NotNull(value.Params);
        var planet = value.Params!.Single(p => p.Position == 0);
        Assert.True(planet.Optional,
            $"{eventName} Event_Param1 is declared required, but the engine treats an empty " +
            "planet list as 'any planet' - so the warning fires on correct data.");
    }

    // The rest of the family's params were already optional. Kept so that flipping position 0 is
    // not mistaken for a licence to relax the whole list.
    [Fact]
    public void TheFilterParam_StaysOptionalToo()
    {
        var value = StoryEventSchema.Value("STORY_ENTER");

        Assert.True(value.Params!.Single(p => p.Position == 1).Optional);
    }
}
