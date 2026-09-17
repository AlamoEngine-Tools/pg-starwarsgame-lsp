// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Debug.PgNet;

namespace PG.StarWarsGame.LSP.Lua.Debug.Protocol;

/// <summary>
///     Encodes and decodes inner messages: a 4-bit magic, a 32-bit id, then the id's fields. Both
///     directions are supported for every id, so a test double of the game can speak it too.
/// </summary>
public interface ILuaMessageCodec
{
    BitBuffer Encode(LuaDebugMessage message);

    /// <exception cref="LuaDebugProtocolException">Wrong magic, unknown id, or fields that end early.</exception>
    LuaDebugMessage Decode(BitBuffer payload);
}
