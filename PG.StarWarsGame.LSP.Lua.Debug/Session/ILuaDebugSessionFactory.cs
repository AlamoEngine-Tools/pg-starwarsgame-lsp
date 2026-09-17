// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <summary>
///     Hands out fresh sessions. A session's connection binds one socket and cannot be reopened,
///     so a launch that retries the handshake needs a new one per attempt.
/// </summary>
public interface ILuaDebugSessionFactory
{
    ILuaDebugSession Create();
}