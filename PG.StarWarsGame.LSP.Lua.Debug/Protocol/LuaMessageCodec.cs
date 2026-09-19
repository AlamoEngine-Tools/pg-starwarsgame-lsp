// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Debug.PgNet;

namespace PG.StarWarsGame.LSP.Lua.Debug.Protocol;

/// <inheritdoc />
public sealed class LuaMessageCodec : ILuaMessageCodec
{
    /// <summary>The 4-bit inner packet-type marker every debugger message starts with.</summary>
    public const uint Magic = 0xD;

    private const int MagicBits = 4;

    /// <summary>
    ///     A split payload reassembled by the reliable layer can arrive with the sender's own
    ///     57-bit outer header (CRC plus packet header) still in front of the magic, all zero.
    /// </summary>
    private const int EmbeddedOuterHeaderBits = 57;

    /// <summary>A thread pair is at least a 32-bit index and a length byte; less than that is padding.</summary>
    private const int MinimumThreadPairBits = 40;

    public BitBuffer Encode(LuaDebugMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var writer = new BitWriter();
        writer.WriteBits(Magic, MagicBits);
        writer.WriteUInt32((uint)message.Id);

        switch (message)
        {
            case HelloMessage or GoodbyeMessage or HeartbeatMessage or RequestScriptListMessage
                or BreakAllMessage or StepOverMessage or StepIntoMessage or StepOutMessage or ContinueMessage:
                break;

            case RequestThreadListMessage m:
                writer.WriteInt32(m.ScriptId);
                break;

            case AddBreakpointMessage m:
                writer.WriteInt32(m.ScriptId);
                writer.WriteInt32(m.ThreadId);
                writer.WriteString(m.SourceName);
                writer.WriteInt32(m.Line);
                writer.WriteString(m.Condition);
                break;

            case RemoveBreakpointMessage m:
                writer.WriteInt32(m.ScriptId);
                writer.WriteInt32(m.ThreadId);
                writer.WriteString(m.SourceName);
                writer.WriteInt32(m.Line);
                break;

            case AttachScriptMessage m:
                writer.WriteInt32(m.ScriptId);
                break;

            case SelectScriptMessage m:
                writer.WriteInt32(m.ScriptId);
                break;

            case BreakThreadMessage m:
                writer.WriteInt32(m.ThreadId);
                break;

            case DumpVariableMessage m:
                writer.WriteInt32(m.ScriptId);
                writer.WriteString(m.VariableName);
                break;

            case DumpTableMessage m:
                writer.WriteInt32(m.ScriptId);
                writer.WriteUInt32(m.RequestId);
                writer.WriteString(m.TableName);
                writer.WriteUInt32((uint)m.Path.Count);
                foreach (var component in m.Path)
                    writer.WriteUInt32(component);
                break;

            case SetCallstackDepthMessage m:
                writer.WriteInt32(m.ScriptId);
                writer.WriteInt32(m.Level);
                break;

            case ExecuteTextMessage m:
                writer.WriteInt32(m.ScriptId);
                writer.WriteString(m.Text);
                break;

            case ScriptListMessage m:
                writer.WriteUInt32((uint)m.Scripts.Count);
                foreach (var script in m.Scripts)
                {
                    writer.WriteInt32(script.ScriptId);
                    writer.WriteString(script.FullPathName);
                }

                break;

            case ThreadListMessage m:
                writer.WriteInt32(m.ScriptId);
                writer.WriteInt32(m.ActiveThreadCount);
                WriteThreads(writer, m.Threads);
                break;

            case ScriptAddedMessage m:
                writer.WriteInt32(m.ScriptId);
                writer.WriteString(m.FullPathName);
                break;

            case ScriptRemovedMessage m:
                writer.WriteInt32(m.ScriptId);
                break;

            case ScriptSuspendedMessage m:
                writer.WriteInt32(m.ScriptId);
                writer.WriteInt32(m.CurrentThreadId);
                writer.WriteString(m.FullPathName);
                writer.WriteUInt32((uint)m.Callstack.Count);
                foreach (var entry in m.Callstack)
                    writer.WriteString(entry);
                writer.WriteInt32(m.ActiveThreadCount);
                WriteThreads(writer, m.Threads);
                break;

            case VariableDumpMessage m:
                writer.WriteInt32(m.ScriptId);
                writer.WriteString(m.VariableName);
                writer.WriteInt32(m.ValueType);
                writer.WriteString(m.ValueText);
                break;

            case TableDumpMessage m:
                writer.WriteUInt32(m.RequestId);
                writer.WriteUInt32((uint)m.Members.Count);
                foreach (var member in m.Members)
                {
                    writer.WriteInt32(member.KeyType);
                    writer.WriteString(member.KeyText);
                    writer.WriteInt32(member.ValueType);
                    writer.WriteString(member.ValueText);
                }

                break;

            case ChildScriptListMessage m:
                writer.WriteInt32(m.ParentScriptId);
                writer.WriteUInt32((uint)m.ChildScriptNames.Count);
                foreach (var name in m.ChildScriptNames)
                    writer.WriteString(name);
                break;

            case OutputMessage m:
                writer.WriteInt32(m.OutputType);
                writer.WriteString(m.Text);
                break;

            case ExecuteTextResponseMessage m:
                writer.WriteInt32(m.ScriptId);
                writer.WriteString(m.ResultText);
                break;

            default:
                throw new ArgumentException($"No encoding for {message.GetType().Name}", nameof(message));
        }

        return writer.ToBuffer();
    }

    public LuaDebugMessage Decode(BitBuffer payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            var reader = ReaderAfterMagic(payload);
            var rawId = reader.ReadUInt32();
            if (!Enum.IsDefined(typeof(LuaMessageId), rawId))
                throw new LuaDebugProtocolException($"Unknown Lua debugger message id {rawId}");

            return DecodeFields((LuaMessageId)rawId, reader);
        }
        catch (PgNetProtocolException e) when (e is not LuaDebugProtocolException)
        {
            throw new LuaDebugProtocolException("Lua debugger message ends early: " + e.Message, e);
        }
    }

    private static LuaDebugMessage DecodeFields(LuaMessageId id, BitReader reader)
    {
        return id switch
        {
            LuaMessageId.Hello => new HelloMessage(),
            LuaMessageId.Goodbye => new GoodbyeMessage(),
            LuaMessageId.Heartbeat => new HeartbeatMessage(),
            LuaMessageId.RequestScriptList => new RequestScriptListMessage(),
            LuaMessageId.RequestThreadList => new RequestThreadListMessage(reader.ReadInt32()),
            LuaMessageId.AddBreakpoint => new AddBreakpointMessage(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadString(), reader.ReadInt32(), reader.ReadString()),
            LuaMessageId.RemoveBreakpoint => new RemoveBreakpointMessage(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadString(), reader.ReadInt32()),
            LuaMessageId.BreakAll => new BreakAllMessage(),
            LuaMessageId.StepOver => new StepOverMessage(),
            LuaMessageId.StepInto => new StepIntoMessage(),
            LuaMessageId.StepOut => new StepOutMessage(),
            LuaMessageId.Continue => new ContinueMessage(),
            LuaMessageId.AttachScript => new AttachScriptMessage(reader.ReadInt32()),
            LuaMessageId.SelectScript => new SelectScriptMessage(reader.ReadInt32()),
            LuaMessageId.BreakThread => new BreakThreadMessage(reader.ReadInt32()),
            LuaMessageId.DumpVariable => new DumpVariableMessage(reader.ReadInt32(), reader.ReadString()),
            LuaMessageId.DumpTable => DecodeDumpTable(reader),
            LuaMessageId.SetCallstackDepth => new SetCallstackDepthMessage(reader.ReadInt32(), reader.ReadInt32()),
            LuaMessageId.ExecuteText => new ExecuteTextMessage(reader.ReadInt32(), reader.ReadString()),
            LuaMessageId.ScriptList => DecodeScriptList(reader),
            LuaMessageId.ThreadList => DecodeThreadList(reader),
            LuaMessageId.ScriptAdded => new ScriptAddedMessage(reader.ReadInt32(), reader.ReadString()),
            LuaMessageId.ScriptRemoved => new ScriptRemovedMessage(reader.ReadInt32()),
            LuaMessageId.ScriptSuspended => DecodeScriptSuspended(reader),
            LuaMessageId.VariableDump => new VariableDumpMessage(
                reader.ReadInt32(), reader.ReadString(), reader.ReadInt32(), reader.ReadString()),
            LuaMessageId.TableDump => DecodeTableDump(reader),
            LuaMessageId.ChildScriptList => DecodeChildScriptList(reader),
            LuaMessageId.Output => new OutputMessage(reader.ReadInt32(), reader.ReadString()),
            LuaMessageId.ExecuteTextResponse => new ExecuteTextResponseMessage(reader.ReadInt32(), reader.ReadString()),
            _ => throw new LuaDebugProtocolException($"Unknown Lua debugger message id {(uint)id}")
        };
    }

    private static BitReader ReaderAfterMagic(BitBuffer payload)
    {
        var reader = payload.CreateReader();
        var magic = reader.ReadBits(MagicBits);
        if (magic == Magic)
            return reader;

        if (payload.BitCount >= EmbeddedOuterHeaderBits + MagicBits)
        {
            var probe = payload.CreateReader();
            var prefix = probe.ReadBits(32) | probe.ReadBits(EmbeddedOuterHeaderBits - 32);
            if (prefix == 0 && probe.ReadBits(MagicBits) == Magic)
                return probe;
        }

        throw new LuaDebugProtocolException($"Unexpected Lua debugger packet magic 0x{magic:x}");
    }

    private static DumpTableMessage DecodeDumpTable(BitReader reader)
    {
        var scriptId = reader.ReadInt32();
        var requestId = reader.ReadUInt32();
        var tableName = reader.ReadString();
        var count = reader.ReadUInt32();
        var path = new List<uint>((int)Math.Min(count, 1024));
        for (uint i = 0; i < count; i++)
            path.Add(reader.ReadUInt32());
        return new DumpTableMessage(scriptId, requestId, tableName, path);
    }

    private static ScriptListMessage DecodeScriptList(BitReader reader)
    {
        var count = reader.ReadUInt32();
        var scripts = new List<ScriptEntry>((int)Math.Min(count, 1024));
        for (uint i = 0; i < count; i++)
            scripts.Add(new ScriptEntry(reader.ReadInt32(), reader.ReadString()));
        return new ScriptListMessage(scripts);
    }

    private static ThreadListMessage DecodeThreadList(BitReader reader)
    {
        var scriptId = reader.ReadInt32();
        var activeThreadCount = reader.ReadInt32();
        return new ThreadListMessage(scriptId, activeThreadCount, ReadThreads(reader));
    }

    private static ScriptSuspendedMessage DecodeScriptSuspended(BitReader reader)
    {
        var scriptId = reader.ReadInt32();
        var currentThreadId = reader.ReadInt32();
        var fullPathName = reader.ReadString();
        var frameCount = reader.ReadUInt32();
        var callstack = new List<string>((int)Math.Min(frameCount, 1024));
        for (uint i = 0; i < frameCount; i++)
            callstack.Add(reader.ReadString());
        var activeThreadCount = reader.ReadInt32();
        return new ScriptSuspendedMessage(
            scriptId, currentThreadId, fullPathName, callstack, activeThreadCount, ReadThreads(reader));
    }

    private static TableDumpMessage DecodeTableDump(BitReader reader)
    {
        var requestId = reader.ReadUInt32();
        var count = reader.ReadUInt32();
        var members = new List<TableMember>((int)Math.Min(count, 1024));
        for (uint i = 0; i < count; i++)
            members.Add(new TableMember(
                reader.ReadInt32(), reader.ReadString(), reader.ReadInt32(), reader.ReadString()));
        return new TableDumpMessage(requestId, members);
    }

    private static ChildScriptListMessage DecodeChildScriptList(BitReader reader)
    {
        var parentScriptId = reader.ReadInt32();
        var count = reader.ReadUInt32();
        var names = new List<string>((int)Math.Min(count, 1024));
        for (uint i = 0; i < count; i++)
            names.Add(reader.ReadString());
        return new ChildScriptListMessage(parentScriptId, names);
    }

    /// <summary>
    ///     Thread pairs carry no count of their own: the game writes one (index, name) per named
    ///     slot and stops. They run to the end of the payload, minus the byte padding.
    /// </summary>
    private static List<ThreadEntry> ReadThreads(BitReader reader)
    {
        var threads = new List<ThreadEntry>();
        while (reader.RemainingBits >= MinimumThreadPairBits)
            threads.Add(new ThreadEntry(reader.ReadInt32(), reader.ReadString()));
        return threads;
    }

    private static void WriteThreads(BitWriter writer, IReadOnlyList<ThreadEntry> threads)
    {
        foreach (var thread in threads)
        {
            if (thread.Name.Length == 0)
                throw new ArgumentException("A thread pair needs a non-empty name; unnamed slots are not sent");
            writer.WriteInt32(thread.ThreadIndex);
            writer.WriteString(thread.Name);
        }
    }
}