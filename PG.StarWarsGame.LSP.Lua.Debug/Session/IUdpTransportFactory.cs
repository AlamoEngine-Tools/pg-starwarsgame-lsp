// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

public interface IUdpTransportFactory
{
    /// <summary>
    ///     Binds a fresh local socket for <paramref name="remote" />. Port 0 lets the system pick,
    ///     which is the normal case: the game rejects a source endpoint it has seen before.
    /// </summary>
    IUdpTransport Open(IPEndPoint remote, int localPort = 0);
}