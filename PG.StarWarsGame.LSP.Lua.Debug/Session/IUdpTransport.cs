// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <summary>
///     One bound UDP socket talking to one remote endpoint. Datagrams from any other endpoint are
///     dropped, so a stale peer can never inject into a session.
/// </summary>
public interface IUdpTransport : IDisposable
{
    IPEndPoint LocalEndPoint { get; }

    IPEndPoint RemoteEndPoint { get; }

    ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken);

    /// <exception cref="LuaDebugConnectionLostException">The peer reset the connection.</exception>
    ValueTask<byte[]> ReceiveAsync(CancellationToken cancellationToken);
}