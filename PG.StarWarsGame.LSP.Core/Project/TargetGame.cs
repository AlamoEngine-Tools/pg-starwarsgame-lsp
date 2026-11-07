// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Project;

/// <summary>The game a project is built for, as its project file names it.</summary>
public enum TargetGame
{
    /// <summary>Empire at War: the base game's data only.</summary>
    EAW,

    /// <summary>Forces of Corruption: the expansion, reading the base game's data beneath its own.</summary>
    FOC
}

/// <summary>The game a project file is given when nothing else decides it.</summary>
public static class TargetGames
{
    /// <summary>
    ///     FoC, the more modded game. It is WRITTEN - by the migration into older files and by every
    ///     project we create - and never read into a file that names none: from the format that
    ///     introduced the field, a project file without it is refused.
    /// </summary>
    public const TargetGame Default = TargetGame.FOC;
}

/// <summary>How a project file spells a <see cref="TargetGame" />: exactly the schema's two values.</summary>
public static class TargetGameNames
{
    public const string Eaw = "eaw";
    public const string Foc = "foc";

    /// <summary>The game a project file's value names, or false for anything the schema refuses.</summary>
    public static bool TryParse(string? value, out TargetGame game)
    {
        switch (value)
        {
            case Eaw:
                game = TargetGame.EAW;
                return true;
            case Foc:
                game = TargetGame.FOC;
                return true;
            default:
                game = TargetGame.FOC;
                return false;
        }
    }

    /// <summary>The value a project file writes for <paramref name="game" />.</summary>
    public static string Of(TargetGame game)
    {
        return game == TargetGame.EAW ? Eaw : Foc;
    }
}
