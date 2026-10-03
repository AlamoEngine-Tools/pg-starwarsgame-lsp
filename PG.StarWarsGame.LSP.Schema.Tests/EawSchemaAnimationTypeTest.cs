// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Schema.Providers;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     <c>AnimationType</c> is the engine's own list of animation names - the one every animation tag
///     is read through.
/// </summary>
/// <remarks>
///     Measured in the engine: all 21 tags that name an animation take it through one converter, which
///     knows 117 display names, spaces included (<c>Turn Left</c>). The enum used to hold 119 names of
///     which only 62 were the engine's - the rest looked like animation file suffixes (<c>TURNL</c>)
///     that the engine never resolves. Vanilla never showed it: every shipped value is one of the 62.
/// </remarks>
public sealed class EawSchemaAnimationTypeTest
{
    private static readonly Lazy<LocalFileSchemaProvider> Shipped = new(() =>
        new LocalFileSchemaProvider(EawSchemaRepo.Root, new FileSystem(),
            NullLogger<LocalFileSchemaProvider>.Instance));

    private static IReadOnlyList<string> Values()
    {
        return Shipped.Value.GetEnum("AnimationType")!.Values.Select(v => v.Name).ToList();
    }

    [Theory]
    [InlineData("Turn Left")]
    [InlineData("Space Idle")]
    [InlineData("Special A")]
    [InlineData("Flinch_Left")]
    [InlineData("Contaminate_Loop")]
    public void AnEngineName_IsAValue(string name)
    {
        Assert.Contains(name, Values(), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("TURNL")]
    [InlineData("SPACE_IDLE")]
    [InlineData("SPECIAL_A")]
    [InlineData("FLINCHB")]
    [InlineData("CONTAIMINATE_LOOP")]
    public void ANameTheEngineDoesNotKnow_IsNot(string name)
    {
        Assert.DoesNotContain(name, Values(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThereIsOneAnimationEnum()
    {
        // A second list would let two tags read the same engine field against different names.
        Assert.Null(Shipped.Value.GetEnum("AnimationState"));
    }

    [Theory]
    [InlineData("ArcSweepAttackAbility", "Attack_Animation")]
    [InlineData("AttackAction", "Attack_Animation_Type")]
    [InlineData("EatAttackAbility", "Attack_Animation")]
    [InlineData("EatAttackAbility", "Target_Grabbed_Animation")]
    [InlineData("GameObjectType", "Specific_Death_Anim_Type")]
    [InlineData("GameObjectType", "Shield_On_Anim")]
    [InlineData("GameObjectType", "Shield_Off_Anim")]
    [InlineData("GenericAttackAbility", "Attack_Animation")]
    [InlineData("GrenadeAttackAbility", "Grenade_Toss_Anim")]
    [InlineData("HeroClashType", "First_Hero_Win_Anim_Type")]
    [InlineData("HeroClashType", "First_Hero_Lose_Anim_Type")]
    [InlineData("HeroClashType", "First_Hero_Draw_Anim_Type")]
    [InlineData("HeroClashType", "First_Hero_Conversation_Anim_Type")]
    [InlineData("HeroClashType", "Second_Hero_Win_Anim_Type")]
    [InlineData("HeroClashType", "Second_Hero_Lose_Anim_Type")]
    [InlineData("HeroClashType", "Second_Hero_Draw_Anim_Type")]
    [InlineData("HeroClashType", "Second_Hero_Conversation_Anim_Type")]
    [InlineData("InfectionAbility", "Shoot_Anim")]
    [InlineData("MoveAction", "Move_Animation_Type")]
    [InlineData("RemoteBombAbility", "Toss_Anim")]
    public void EveryAnimationTag_ReadsTheEngineList(string owner, string tag)
    {
        var definition = Shipped.Value.GetTagsForType(owner)
            .Single(t => t.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));

        Assert.Equal("AnimationType", definition.Enum?.Name);
    }

    [Fact]
    public void PresenceInducedAnimations_KeysOnTheSameList()
    {
        Assert.Equal("AnimationType", Shipped.Value.GetTag("Presence_Induced_Animations")!.Slots[0].Enum?.Name);
    }
}