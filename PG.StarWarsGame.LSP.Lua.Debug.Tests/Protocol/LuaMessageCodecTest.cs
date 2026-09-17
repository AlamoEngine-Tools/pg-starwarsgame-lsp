// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Lua.Debug.PgNet;
using PG.StarWarsGame.LSP.Lua.Debug.Protocol;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.Protocol;

public sealed class LuaMessageCodecTest
{
    private readonly ILuaMessageCodec _codec;
    private readonly IPgNetDatagramCodec _datagrams;

    public LuaMessageCodecTest()
    {
        var provider = TestServices.Build();
        _codec = provider.GetRequiredService<ILuaMessageCodec>();
        _datagrams = provider.GetRequiredService<IPgNetDatagramCodec>();
    }

    /// <summary>Every message once, with data that exercises each field kind including -1 ids.</summary>
    public static TheoryData<LuaDebugMessage> EveryMessage()
    {
        return
        [
            new HelloMessage(),
            new GoodbyeMessage(),
            new HeartbeatMessage(),
            new RequestScriptListMessage(),
            new RequestThreadListMessage(343),
            new AddBreakpointMessage(-1, -1, "Data\\Scripts\\Foo.lua", 33, ""),
            new AddBreakpointMessage(7, 2, "Data/Scripts/Foo.lua", 33, "x > 0"),
            new RemoveBreakpointMessage(-1, -1, "Data\\Scripts\\Foo.lua", 33),
            new BreakAllMessage(),
            new StepOverMessage(),
            new StepIntoMessage(),
            new StepOutMessage(),
            new ContinueMessage(),
            new AttachScriptMessage(0x1234),
            new SelectScriptMessage(9),
            new BreakThreadMessage(-1),
            new DumpVariableMessage(7, "planet"),
            new DumpTableMessage(7, 0xFFFFFFFF, "SmallTable", [1, 0, 3]),
            new DumpTableMessage(7, 1, "Flat", []),
            new SetCallstackDepthMessage(7, 2),
            new ExecuteTextMessage(7, "__debug_result = object.Get_Name()"),
            new ScriptListMessage([new ScriptEntry(1, "Data\\Scripts\\A.lua"), new ScriptEntry(2, "Data\\Scripts\\B.lua")]),
            new ScriptListMessage([]),
            new ThreadListMessage(7, 3, [new ThreadEntry(0, "main"), new ThreadEntry(4, "Story_Thread")]),
            new ThreadListMessage(7, 0, []),
            new ScriptAddedMessage(8, "Data\\Scripts\\C.lua"),
            new ScriptRemovedMessage(8),
            new ScriptSuspendedMessage(7, -1, "Data\\Scripts\\A.lua",
                ["Data\\Scripts\\A.lua:12:main::", "Data\\Scripts\\A.lua:30:Lua:global:Tick"], 1,
                [new ThreadEntry(0, "main")]),
            new ScriptSuspendedMessage(7, 2, "Data\\Scripts\\A.lua", [], 0, []),
            new VariableDumpMessage(7, "planet", 5, "table: 0x0A1B2C3D"),
            new VariableDumpMessage(99, "missing", -1, ""),
            new TableDumpMessage(1, [new TableMember(4, "name", 4, "Coruscant"), new TableMember(3, "1", 3, "42")]),
            new TableDumpMessage(2, []),
            new ChildScriptListMessage(7, ["Data\\Scripts\\Library\\PGBase.lua", "Data\\Scripts\\Library\\PGDebug.lua"]),
            new ChildScriptListMessage(7, []),
            new OutputMessage(0, "LuaScript: \"A\", Warning: something\n"),
            new OutputMessage(1, "type = number, value = 1.000000"),
            new ExecuteTextResponseMessage(7, "\r\n> ")
        ];
    }

    /// <summary>The payload as the reliable layer delivers it: through a datagram, padding bits included.</summary>
    private BitBuffer AsDelivered(BitBuffer payload)
    {
        return _datagrams.Decode(_datagrams.Encode(new PgNetPacket(0, PgNetPacketKind.Guaranteed, false, payload))).Payload;
    }

    // -- vectors ------------------------------------------------------------------------------

    [Fact]
    public void Encode_Hello_IsMagicThenIdOne()
    {
        Assert.Equal(Convert.FromHexString("1d00000000"), _codec.Encode(new HelloMessage()).ToArray());
        Assert.Equal(36, _codec.Encode(new HelloMessage()).BitCount);
    }

    [Fact]
    public void Encode_RequestScriptList_IsMagicThenIdThree()
    {
        Assert.Equal(Convert.FromHexString("3d00000000"), _codec.Encode(new RequestScriptListMessage()).ToArray());
    }

    [Fact]
    public void Encode_AttachScript_WritesMagicIdAndScriptId()
    {
        var reader = _codec.Encode(new AttachScriptMessage(0x1234)).CreateReader();

        Assert.Equal(0xDu, reader.ReadBits(4));
        Assert.Equal(14u, reader.ReadUInt32());
        Assert.Equal(0x1234u, reader.ReadUInt32());
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void Encode_AddBreakpoint_WritesFieldsInWireOrder()
    {
        var reader = _codec.Encode(new AddBreakpointMessage(-1, -1, "Foo.lua", 33, "")).CreateReader();
        reader.ReadBits(4);

        Assert.Equal(7u, reader.ReadUInt32());
        Assert.Equal(-1, reader.ReadInt32());
        Assert.Equal(-1, reader.ReadInt32());
        Assert.Equal("Foo.lua", reader.ReadString());
        Assert.Equal(33, reader.ReadInt32());
        Assert.Equal("", reader.ReadString());
        Assert.Equal(0, reader.RemainingBits);
    }

    // -- round trips --------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void Decode_OfEncode_ReproducesTheMessage(LuaDebugMessage message)
    {
        var decoded = _codec.Decode(_codec.Encode(message));

        Assert.Equal(message.Id, decoded.Id);
        Assert.Equal(message.GetType(), decoded.GetType());
        Assert.Equal(_codec.Encode(message).ToArray(), _codec.Encode(decoded).ToArray());
    }

    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void Decode_OfDeliveredPayloadWithPadding_ReproducesTheMessage(LuaDebugMessage message)
    {
        var decoded = _codec.Decode(AsDelivered(_codec.Encode(message)));

        Assert.Equal(message.GetType(), decoded.GetType());
        Assert.Equal(_codec.Encode(message).ToArray(), _codec.Encode(decoded).ToArray());
    }

    [Fact]
    public void Decode_ScriptSuspended_ReadsEveryField()
    {
        var message = new ScriptSuspendedMessage(7, 2, "Data\\Scripts\\A.lua",
            ["Data\\Scripts\\A.lua:12:main::", "Data\\Scripts\\A.lua:30:Lua:global:Tick"], 3,
            [new ThreadEntry(0, "main"), new ThreadEntry(2, "Story_Thread")]);

        var decoded = Assert.IsType<ScriptSuspendedMessage>(_codec.Decode(AsDelivered(_codec.Encode(message))));

        Assert.Equal(7, decoded.ScriptId);
        Assert.Equal(2, decoded.CurrentThreadId);
        Assert.Equal("Data\\Scripts\\A.lua", decoded.FullPathName);
        Assert.Equal(message.Callstack, decoded.Callstack);
        Assert.Equal(3, decoded.ActiveThreadCount);
        Assert.Equal(message.Threads, decoded.Threads);
    }

    [Fact]
    public void Decode_ThreadListWithPadding_DoesNotReadPaddingAsAThread()
    {
        var message = new ThreadListMessage(7, 1, [new ThreadEntry(4, "Story_Thread")]);

        var decoded = Assert.IsType<ThreadListMessage>(_codec.Decode(AsDelivered(_codec.Encode(message))));

        Assert.Equal([new ThreadEntry(4, "Story_Thread")], decoded.Threads);
    }

    [Fact]
    public void Decode_DumpTable_ReadsRequestIdAndPath()
    {
        var decoded = Assert.IsType<DumpTableMessage>(
            _codec.Decode(_codec.Encode(new DumpTableMessage(7, 0xFFFFFFFF, "T", [1, 0, 3]))));

        Assert.Equal(7, decoded.ScriptId);
        Assert.Equal(0xFFFFFFFFu, decoded.RequestId);
        Assert.Equal("T", decoded.TableName);
        Assert.Equal([1u, 0u, 3u], decoded.Path);
    }

    [Fact]
    public void Decode_TableDump_ReadsMembers()
    {
        var members = new[] { new TableMember(4, "name", 4, "Coruscant"), new TableMember(3, "1", 5, "table: 0x1") };

        var decoded = Assert.IsType<TableDumpMessage>(_codec.Decode(_codec.Encode(new TableDumpMessage(9, members))));

        Assert.Equal(9u, decoded.RequestId);
        Assert.Equal(members, decoded.Members);
    }

    [Fact]
    public void Decode_VariableDumpWithUnknownScript_KeepsMinusOneType()
    {
        var decoded = Assert.IsType<VariableDumpMessage>(
            _codec.Decode(_codec.Encode(new VariableDumpMessage(99, "x", -1, ""))));

        Assert.Equal(-1, decoded.ValueType);
    }

    // -- framing tolerance and rejection ------------------------------------------------------

    [Fact]
    public void Decode_ZeroOuterHeaderInFrontOfMagic_IsSkipped()
    {
        var writer = new BitWriter();
        writer.WriteBits(0, 32);
        writer.WriteBits(0, 25);
        writer.WriteBuffer(_codec.Encode(new HelloMessage()));

        Assert.IsType<HelloMessage>(_codec.Decode(writer.ToBuffer()));
    }

    [Fact]
    public void Decode_WrongMagic_Throws()
    {
        var writer = new BitWriter();
        writer.WriteBits(0x3, 4);
        writer.WriteUInt32(1);

        var e = Assert.Throws<LuaDebugProtocolException>(() => _codec.Decode(writer.ToBuffer()));
        Assert.Contains("magic", e.Message);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(5u)]
    [InlineData(6u)]
    [InlineData(18u)]
    [InlineData(22u)]
    [InlineData(23u)]
    [InlineData(26u)]
    [InlineData(36u)]
    [InlineData(99u)]
    public void Decode_IdOutsideTheKnownSet_Throws(uint id)
    {
        var writer = new BitWriter();
        writer.WriteBits(LuaMessageCodec.Magic, 4);
        writer.WriteUInt32(id);

        var e = Assert.Throws<LuaDebugProtocolException>(() => _codec.Decode(writer.ToBuffer()));
        Assert.Contains(id.ToString(), e.Message);
    }

    [Fact]
    public void Decode_TruncatedFields_ThrowsTheLuaException()
    {
        var writer = new BitWriter();
        writer.WriteBits(LuaMessageCodec.Magic, 4);
        writer.WriteUInt32((uint)LuaMessageId.ScriptAdded);
        writer.WriteInt32(8);
        // The path string is missing entirely.

        var e = Assert.Throws<LuaDebugProtocolException>(() => _codec.Decode(writer.ToBuffer()));
        Assert.IsAssignableFrom<PgNetProtocolException>(e);
    }

    [Fact]
    public void Encode_ThreadWithEmptyName_Throws()
    {
        var message = new ThreadListMessage(7, 1, [new ThreadEntry(0, "")]);

        Assert.Throws<ArgumentException>(() => _codec.Encode(message));
    }

    [Fact]
    public void Encode_StringOf255Bytes_Throws()
    {
        Assert.Throws<ArgumentException>(() => _codec.Encode(new ExecuteTextMessage(1, new string('x', 255))));
    }
}
