// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <inheritdoc />
public sealed class ConnectHandshake : IConnectHandshake
{
    public const uint Magic = 0xF000F000;
    public const string ClientGreeting = "Yo!";
    public const string ServerGreeting = "Spoot";

    private readonly IPgNetDatagramCodec _codec;

    public ConnectHandshake(IPgNetDatagramCodec codec)
    {
        _codec = codec;
    }

    public byte[] BuildRequest(string clientName)
    {
        var writer = new BitWriter();
        writer.WriteUInt32(Magic);
        writer.WriteString(ClientGreeting);
        writer.WriteString(clientName);
        return _codec.Encode(
            new PgNetPacket(PgNetPacket.MaxPacketId, PgNetPacketKind.NonGuaranteed, false, writer.ToBuffer()));
    }

    public ConnectResponse ParseResponse(ReadOnlySpan<byte> datagram)
    {
        var packet = _codec.Decode(datagram);
        var reader = packet.Payload.CreateReader();
        var magic = reader.ReadUInt32();
        var greeting = reader.ReadString();
        var serverName = reader.ReadString();
        if (magic != Magic)
            throw new PgNetProtocolException($"Unexpected connect magic 0x{magic:x8}");
        if (greeting != ServerGreeting)
            throw new PgNetProtocolException($"Unexpected connect greeting '{greeting}'");
        return new ConnectResponse(packet, serverName);
    }
}