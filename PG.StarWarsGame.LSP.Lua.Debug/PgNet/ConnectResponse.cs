// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <summary>
///     The game's answer to the connect request. The packet is kept because a guaranteed reply
///     must be acknowledged by the reliable layer like any other.
/// </summary>
public sealed record ConnectResponse(PgNetPacket Packet, string ServerName);