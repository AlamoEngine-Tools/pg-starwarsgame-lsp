// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <inheritdoc />
public sealed class UdpTransportFactory : IUdpTransportFactory
{
    public IUdpTransport Open(IPEndPoint remote, int localPort = 0)
    {
        return new UdpTransport(remote, localPort);
    }
}