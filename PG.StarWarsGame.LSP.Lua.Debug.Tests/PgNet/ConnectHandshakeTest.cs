// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Debug.PgNet;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.PgNet;

public sealed class ConnectHandshakeTest
{
    // Captured from a live session: the request a stock client sends and the reply the game gives.
    private static readonly byte[] KnownRequest =
        Convert.FromHexString("04cc6158ffff7f00e001e007b2de422898eac288cac4eacececae49c8aa87464606a707000");

    private static readonly byte[] KnownResponse =
        Convert.FromHexString("3d3c16e500000000e001e00ba6e0dedee81ca6e8c2e4aec2e4e692746e70706800");

    private readonly IPgNetDatagramCodec _codec = TestServices.Get<IPgNetDatagramCodec>();
    private readonly IConnectHandshake _handshake = TestServices.Get<IConnectHandshake>();

    [Fact]
    public void BuildRequest_KnownClientName_MatchesCapturedBytes()
    {
        var request = _handshake.BuildRequest("LuaDebuggerNET:20588");

        Assert.Equal(KnownRequest, request);
    }

    [Fact]
    public void BuildRequest_IsNonGuaranteedWithAllOnesId()
    {
        var packet = _codec.Decode(_handshake.BuildRequest("AetLuaDebugger:4242"));

        Assert.Equal(PgNetPacket.MaxPacketId, packet.PacketId);
        Assert.Equal(PgNetPacketKind.NonGuaranteed, packet.Kind);
        Assert.False(packet.Resend);

        var payload = packet.Payload.CreateReader();
        Assert.Equal(ConnectHandshake.Magic, payload.ReadUInt32());
        Assert.Equal("Yo!", payload.ReadString());
        Assert.Equal("AetLuaDebugger:4242", payload.ReadString());
    }

    [Fact]
    public void ParseResponse_CapturedSpoot_ReturnsServerNameAndPacket()
    {
        var response = _handshake.ParseResponse(KnownResponse);

        Assert.Equal("StarWarsI:7884", response.ServerName);
        Assert.Equal(0u, response.Packet.PacketId);
        Assert.Equal(PgNetPacketKind.Guaranteed, response.Packet.Kind);
    }

    [Fact]
    public void ParseResponse_BadCrc_Throws()
    {
        var corrupted = (byte[])KnownResponse.Clone();
        corrupted[^1] ^= 0x01;

        Assert.Throws<PgNetCrcException>(() => _handshake.ParseResponse(corrupted));
    }

    [Fact]
    public void ParseResponse_WrongGreeting_Throws()
    {
        var datagram = Response(ConnectHandshake.Magic, "Spoof");

        Assert.Throws<PgNetProtocolException>(() => _handshake.ParseResponse(datagram));
    }

    [Fact]
    public void ParseResponse_WrongMagic_Throws()
    {
        var datagram = Response(0xDEADBEEF, ConnectHandshake.ServerGreeting);

        Assert.Throws<PgNetProtocolException>(() => _handshake.ParseResponse(datagram));
    }

    [Fact]
    public void BuildRequest_NameOf255Bytes_Throws()
    {
        Assert.Throws<ArgumentException>(() => _handshake.BuildRequest(new string('n', 255)));
    }

    private byte[] Response(uint magic, string greeting)
    {
        var writer = new BitWriter();
        writer.WriteUInt32(magic);
        writer.WriteString(greeting);
        writer.WriteString("StarWarsI:7884");
        return _codec.Encode(new PgNetPacket(0, PgNetPacketKind.Guaranteed, false, writer.ToBuffer()));
    }
}