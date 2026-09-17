// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Protocol;

// Messages the debugger sends to the game, plus the three that travel both ways.

public sealed record HelloMessage : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.Hello;
}

public sealed record GoodbyeMessage : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.Goodbye;
}

public sealed record HeartbeatMessage : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.Heartbeat;
}

public sealed record RequestScriptListMessage : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.RequestScriptList;
}

public sealed record RequestThreadListMessage(int ScriptId) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.RequestThreadList;
}

/// <summary>
///     Script id -1 with thread id -1 is a source-wide breakpoint that matches every live script.
///     The condition is carried on the wire; whether the game evaluates it is not this layer's concern.
/// </summary>
public sealed record AddBreakpointMessage(int ScriptId, int ThreadId, string SourceName, int Line, string Condition)
    : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.AddBreakpoint;
}

public sealed record RemoveBreakpointMessage(int ScriptId, int ThreadId, string SourceName, int Line)
    : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.RemoveBreakpoint;
}

public sealed record BreakAllMessage : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.BreakAll;
}

public sealed record StepOverMessage : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.StepOver;
}

public sealed record StepIntoMessage : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.StepInto;
}

public sealed record StepOutMessage : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.StepOut;
}

public sealed record ContinueMessage : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.Continue;
}

public sealed record AttachScriptMessage(int ScriptId) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.AttachScript;
}

public sealed record SelectScriptMessage(int ScriptId) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.SelectScript;
}

/// <summary>Thread id -1 means the script's main state.</summary>
public sealed record BreakThreadMessage(int ThreadId) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.BreakThread;
}

public sealed record DumpVariableMessage(int ScriptId, string VariableName) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.DumpVariable;
}

/// <summary>
///     <paramref name="RequestId" /> is an opaque 32-bit value the game hands back in the reply;
///     <paramref name="Path" /> descends into nested tables by member index.
/// </summary>
public sealed record DumpTableMessage(int ScriptId, uint RequestId, string TableName, IReadOnlyList<uint> Path)
    : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.DumpTable;
}

/// <summary><paramref name="Level" /> indexes the call stack in the order the game sent it, outermost frame first.</summary>
public sealed record SetCallstackDepthMessage(int ScriptId, int Level) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.SetCallstackDepth;
}

public sealed record ExecuteTextMessage(int ScriptId, string Text) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.ExecuteText;
}
