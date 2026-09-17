// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <summary>
///     The game is gone: it said goodbye, reset the socket, or fell silent for longer than the
///     heartbeat allows. Every request in flight fails with this.
/// </summary>
public sealed class LuaDebugConnectionLostException : Exception
{
    public LuaDebugConnectionLostException(string message) : base(message)
    {
    }

    public LuaDebugConnectionLostException(string message, Exception innerException) : base(message, innerException)
    {
    }
}