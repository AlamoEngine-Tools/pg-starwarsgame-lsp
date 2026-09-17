// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Threading.Channels;
using PG.StarWarsGame.LSP.Lua.Debug.Protocol;

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <summary>
///     One live link to the game's debug server: the connect handshake, the debugger hello, a
///     receive loop that acknowledges and orders packets, a timer that resends and watches for
///     silence, and a goodbye on the way out. Inbound messages arrive in order on
///     <see cref="Inbound" />; the reader completes with <see cref="LuaDebugConnectionLostException" />
///     when the game is gone and cleanly after a disconnect.
/// </summary>
public interface ILuaDebugConnection : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>The name the game reported in its connect reply, for diagnostics only.</summary>
    string? ServerName { get; }

    IPEndPoint? LocalEndPoint { get; }

    /// <summary>No datagram of any kind for this long means the game is gone. Heartbeats count.</summary>
    TimeSpan SilenceTimeout { get; set; }

    ChannelReader<LuaDebugMessage> Inbound { get; }

    /// <exception cref="TimeoutException">The game did not answer the handshake in time.</exception>
    Task ConnectAsync(IPEndPoint remote, string clientName, TimeSpan timeout, CancellationToken cancellationToken);

    Task SendAsync(LuaDebugMessage message, CancellationToken cancellationToken);

    /// <summary>Sends goodbye, waits for the game to acknowledge what is still pending, then closes.</summary>
    Task DisconnectAsync(TimeSpan timeout, CancellationToken cancellationToken);
}