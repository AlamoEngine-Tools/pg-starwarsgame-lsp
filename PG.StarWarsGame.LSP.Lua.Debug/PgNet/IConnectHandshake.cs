// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <summary>
///     The core connection handshake that precedes any debugger traffic. The client sends one
///     non-guaranteed datagram with the all-ones packet id carrying the magic, the greeting and its
///     name; the game answers with the same magic, its own greeting and its name. Only after that
///     reply may reliable packets flow.
/// </summary>
public interface IConnectHandshake
{
    byte[] BuildRequest(string clientName);

    /// <summary>
    ///     Validates the CRC, the magic and the greeting. The server name is diagnostic (it carries
    ///     the game's process id) and is returned, never matched.
    /// </summary>
    ConnectResponse ParseResponse(ReadOnlySpan<byte> datagram);
}
