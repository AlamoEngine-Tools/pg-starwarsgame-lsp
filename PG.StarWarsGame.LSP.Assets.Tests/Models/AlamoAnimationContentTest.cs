// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     Pairing an animation with a model, and saying why when it does not pair.
/// </summary>
/// <remarks>
///     The reason is not decoration. A dropped clip is indistinguishable from a missing file once it
///     is gone, and the preview silently lost 27 of the AT-AT's 30 animations - every die among them
///     - with nothing anywhere to say so.
/// </remarks>
public sealed class AlamoAnimationContentTest
{
    private static AlamoAnimationContent Animation(params (int Index, string Name)[] bones)
    {
        return new AlamoAnimationContent(30f, 2,
            [.. bones.Select(b => new AlamoAnimationBone(b.Index, b.Name, []))], 1);
    }

    [Fact]
    public void WhyNotModel_WhenEveryBoneLinesUp_IsSilent()
    {
        var animation = Animation((0, "Root"), (2, "B_Head"));

        Assert.Null(animation.WhyNotModel(["Root", "B_Body", "B_Head"]));
    }

    /// <summary>
    ///     The engine uppercases bone names and the files do not, so the corpus disagrees with itself
    ///     on case constantly - `EV_AT-AT.alo` alone spells its own bones both ways.
    /// </summary>
    [Fact]
    public void WhyNotModel_IgnoresCase()
    {
        Assert.Null(Animation((1, "b_body")).WhyNotModel(["Root", "B_BODY"]));
    }

    /// <summary>
    ///     The case that cost the AT-AT its die animations. A clip may legitimately drive a bone past
    ///     the end of the SKELETON, because the engine assumes a bone at the origin of every mesh -
    ///     see the mesh-names-are-bones rule. The caller decides which list to pass; this only has to
    ///     report the mismatch clearly enough to tell the two apart.
    /// </summary>
    [Fact]
    public void WhyNotModel_ForABoneBeyondTheList_NamesTheIndexAndTheSize()
    {
        var why = Animation((7, "Muzzleflash")).WhyNotModel(["Root", "B_Body"]);

        Assert.NotNull(why);
        Assert.Contains("7", why);
        Assert.Contains("Muzzleflash", why);
        Assert.Contains("2", why);
    }

    [Fact]
    public void WhyNotModel_ForADifferentBoneAtThatIndex_NamesBothSpellings()
    {
        var why = Animation((1, "B_Arm")).WhyNotModel(["Root", "B_Body"]);

        Assert.NotNull(why);
        Assert.Contains("B_Arm", why);
        Assert.Contains("B_Body", why);
    }

    /// <summary>
    ///     One is defined by the other, so they can never disagree about a pairing - which is the
    ///     whole point of having replaced the boolean with a reason.
    /// </summary>
    [Theory]
    [InlineData(0, "Root", true)]
    [InlineData(1, "B_Body", true)]
    [InlineData(1, "B_Arm", false)]
    [InlineData(9, "B_Body", false)]
    public void MatchesModel_AgreesWithWhyNotModel(int index, string name, bool expected)
    {
        var animation = Animation((index, name));
        string[] model = ["Root", "B_Body"];

        Assert.Equal(expected, animation.MatchesModel(model));
        Assert.Equal(expected, animation.WhyNotModel(model) is null);
    }

    /// <summary>
    ///     Whether the clip can be APPLIED at all, which is a weaker question than whether it pairs.
    /// </summary>
    /// <remarks>
    ///     Reported by the user: "'X' takes its animations from 'Y.alo', but that model's skeleton
    ///     differs" fires all over the BASE GAME, on units that animate perfectly well in the engine.
    ///     So an identical skeleton is not what the engine requires, and treating a name disagreement
    ///     as fatal dropped clips the game plays.
    ///
    ///     An index past the end of the bone list is different in kind: there is no node to drive, so
    ///     the track cannot be written whatever anyone decides about names.
    /// </remarks>
    [Fact]
    public void FitsSkeleton_WithADifferentNameAtThatIndex_IsStillPlayable()
    {
        var animation = Animation((1, "B_Arm"));

        Assert.True(animation.FitsSkeleton(["Root", "B_Body"]));
        Assert.False(animation.MatchesModel(["Root", "B_Body"]));
    }

    [Fact]
    public void FitsSkeleton_ForABoneBeyondTheList_IsFalse()
    {
        Assert.False(Animation((7, "Muzzleflash")).FitsSkeleton(["Root", "B_Body"]));
    }

    [Fact]
    public void FitsSkeleton_ForANegativeIndex_IsFalse()
    {
        Assert.False(Animation((-1, "Root")).FitsSkeleton(["Root", "B_Body"]));
    }

    [Fact]
    public void FitsSkeleton_WhenEveryBoneLinesUp_IsTrue()
    {
        Assert.True(Animation((0, "Root"), (1, "B_Body")).FitsSkeleton(["Root", "B_Body"]));
    }

    [Fact]
    public void FitsSkeleton_WithoutAModel_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Animation((0, "Root")).FitsSkeleton(null!));
    }

    [Fact]
    public void WhyNotModel_WithoutAModel_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Animation((0, "Root")).WhyNotModel(null!));
    }
}
