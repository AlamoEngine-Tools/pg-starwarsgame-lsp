// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.LSP.Core.Project;

namespace PG.StarWarsGame.LSP.Localisation;

/// <summary>Which shipped master text files a project's game resolves keys against.</summary>
public static class BaselineMasterTexts
{
    /// <summary>
    ///     The base game's text for Empire at War. For Forces of Corruption the base game's and then
    ///     the expansion's, as before the target game existed.
    /// </summary>
    public static IReadOnlyList<GameContext> Contexts(TargetGame game)
    {
        return game == TargetGame.EAW ? [GameContext.EaW] : [GameContext.EaW, GameContext.FoC];
    }
}
