// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.LSP.Core.Project;

namespace PG.StarWarsGame.LSP.Localisation.Tests;

public sealed class BaselineMasterTextsTest
{
    // The base game ships no expansion text, so an EaW project resolves none of it.
    [Fact]
    public void Contexts_Eaw_IsTheBaseGameOnly()
    {
        Assert.Equal([GameContext.EaW], BaselineMasterTexts.Contexts(TargetGame.EAW));
    }

    // Unchanged from before the target game existed: the base game's text, then the expansion's.
    [Fact]
    public void Contexts_Foc_IsBothGamesBaseFirst()
    {
        Assert.Equal([GameContext.EaW, GameContext.FoC], BaselineMasterTexts.Contexts(TargetGame.FOC));
    }
}
