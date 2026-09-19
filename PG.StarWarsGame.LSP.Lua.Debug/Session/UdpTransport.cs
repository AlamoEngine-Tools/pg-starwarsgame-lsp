// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Net.Sockets;

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <inheritdoc />
public sealed class UdpTransport : IUdpTransport
{
    /// <summary>
    ///     Windows reports an ICMP "port unreachable" for an earlier send as a reset on the next
    ///     receive of an unconnected UDP socket. This control code turns that off, so a game that
    ///     has not started listening yet does not kill the socket that will talk to it.
    /// </summary>
    private const int SioUdpConnReset = -1744830452;

    private readonly UdpClient _client;

    public UdpTransport(IPEndPoint remote, int localPort)
    {
        RemoteEndPoint = remote;
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, localPort));
        _client.Client.ReceiveBufferSize = 1024 * 1024;
        if (OperatingSystem.IsWindows())
            _client.Client.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
        LocalEndPoint = (IPEndPoint)_client.Client.LocalEndPoint!;
    }

    public IPEndPoint LocalEndPoint { get; }

    public IPEndPoint RemoteEndPoint { get; }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken)
    {
        await _client.SendAsync(datagram, RemoteEndPoint, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<byte[]> ReceiveAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            UdpReceiveResult received;
            try
            {
                received = await _client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
            {
                throw new LuaDebugConnectionLostException("The game reset the debugger connection", e);
            }
            catch (ObjectDisposedException)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (received.RemoteEndPoint.Equals(RemoteEndPoint))
                return received.Buffer;
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}