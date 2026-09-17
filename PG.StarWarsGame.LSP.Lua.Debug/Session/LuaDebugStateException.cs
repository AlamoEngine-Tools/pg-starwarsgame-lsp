// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <summary>
///     A command the game would reject in its current state: breaking while suspended, continuing
///     while running, selecting a frame outside the stack, naming a script or thread it does not
///     have. The session refuses before anything reaches the wire, because on the game side these
///     are assertions, and in a debug build an assertion is a dialog or a dead process.
/// </summary>
public sealed class LuaDebugStateException : InvalidOperationException
{
    public LuaDebugStateException(string message) : base(message)
    {
    }
}
