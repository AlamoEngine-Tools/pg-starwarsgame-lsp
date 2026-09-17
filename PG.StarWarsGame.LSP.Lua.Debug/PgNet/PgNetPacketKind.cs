// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <summary>The 2-bit base packet type in every datagram header.</summary>
public enum PgNetPacketKind
{
    /// <summary>Reliable: retained by the sender until the peer acknowledges its id.</summary>
    Guaranteed = 0,

    /// <summary>Fire and forget; the connect request is the only one the debugger sends.</summary>
    NonGuaranteed = 1,

    /// <summary>Acknowledges the guaranteed packet with the same id. No payload.</summary>
    Ack = 2,

    /// <summary>Asks the peer to resend the guaranteed packet with this id. No payload.</summary>
    Nack = 3
}