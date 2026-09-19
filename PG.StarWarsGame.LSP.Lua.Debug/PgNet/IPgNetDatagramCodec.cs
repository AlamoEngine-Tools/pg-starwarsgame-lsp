// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <summary>
///     Datagram framing. Every datagram is a little-endian CRC-32 over bytes 4 onward, then a
///     bit-packed body: 22-bit packet id, 2-bit <see cref="PgNetPacketKind" />, 1-bit resend flag,
///     payload bits, zero padding to the byte boundary.
/// </summary>
public interface IPgNetDatagramCodec
{
    byte[] Encode(PgNetPacket packet);

    /// <summary>
    ///     Decodes one datagram. A decoded payload keeps its padding bits up to the byte boundary;
    ///     the inner message parser reads what it needs and ignores the rest.
    /// </summary>
    /// <exception cref="PgNetCrcException">The stored CRC does not match the body.</exception>
    /// <exception cref="PgNetProtocolException">The datagram is shorter than a header.</exception>
    PgNetPacket Decode(ReadOnlySpan<byte> datagram, bool validateCrc = true);

    byte[] EncodeAck(uint packetId);

    byte[] EncodeNack(uint packetId);
}