// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <summary>The game-side debugger state as the session mirrors it.</summary>
public enum DebugRunState
{
    /// <summary>Nothing armed, nothing suspended; the game runs freely.</summary>
    Running,

    /// <summary>A break, thread break or step is armed; the game runs until a script reaches a Lua line that satisfies it.</summary>
    BreakArmed,

    /// <summary>A script is stopped inside the line hook and the whole game is frozen with it.</summary>
    Suspended
}