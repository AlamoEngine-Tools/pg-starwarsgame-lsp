// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Protocol;

// Messages the game sends to the debugger.

public sealed record ScriptListMessage(IReadOnlyList<ScriptEntry> Scripts) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.ScriptList;
}

/// <summary>
///     <paramref name="ActiveThreadCount" /> is the game's own count; <paramref name="Threads" />
///     lists only the named slots, which can be fewer.
/// </summary>
public sealed record ThreadListMessage(int ScriptId, int ActiveThreadCount, IReadOnlyList<ThreadEntry> Threads)
    : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.ThreadList;
}

public sealed record ScriptAddedMessage(int ScriptId, string FullPathName) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.ScriptAdded;
}

public sealed record ScriptRemovedMessage(int ScriptId) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.ScriptRemoved;
}

/// <summary>
///     A script stopped. <paramref name="Callstack" /> holds one formatted entry per frame,
///     outermost first, in the shape <c>source:line:what:namewhat:name</c>.
/// </summary>
public sealed record ScriptSuspendedMessage(
    int ScriptId,
    int CurrentThreadId,
    string FullPathName,
    IReadOnlyList<string> Callstack,
    int ActiveThreadCount,
    IReadOnlyList<ThreadEntry> Threads) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.ScriptSuspended;
}

/// <summary><paramref name="ValueType" /> is the Lua type code; -1 when the script id was unknown.</summary>
public sealed record VariableDumpMessage(int ScriptId, string VariableName, int ValueType, string ValueText)
    : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.VariableDump;
}

public sealed record TableDumpMessage(uint RequestId, IReadOnlyList<TableMember> Members) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.TableDump;
}

/// <summary>The reply to an attach request: the sources loaded under that script.</summary>
public sealed record ChildScriptListMessage(int ParentScriptId, IReadOnlyList<string> ChildScriptNames)
    : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.ChildScriptList;
}

/// <summary><paramref name="OutputType" /> 0 is a script log line, 1 the console print.</summary>
public sealed record OutputMessage(int OutputType, string Text) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.Output;
}

public sealed record ExecuteTextResponseMessage(int ScriptId, string ResultText) : LuaDebugMessage
{
    public override LuaMessageId Id => LuaMessageId.ExecuteTextResponse;
}