// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Debug.PgNet;

namespace PG.StarWarsGame.LSP.Lua.Debug.Protocol;

/// <summary>
///     An inner message that does not follow the debugger's wire format: wrong magic, an id
///     outside the known set, or fields that end early. One kind with the transport's exception so
///     a connection can drop either with a single catch.
/// </summary>
public sealed class LuaDebugProtocolException : PgNetProtocolException
{
    public LuaDebugProtocolException(string message) : base(message)
    {
    }

    public LuaDebugProtocolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}