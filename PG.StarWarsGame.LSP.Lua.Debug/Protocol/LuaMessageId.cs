// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Protocol;

/// <summary>
///     The 32-bit message id that follows the 4-bit inner magic. Ids the game recognises but
///     treats as no-ops (5, 6, 18, 22, 23, 26) are deliberately absent: nothing sends them, and a
///     stray one is reported rather than silently accepted.
/// </summary>
public enum LuaMessageId : uint
{
    /// <summary>Both directions. The client sends it after the connect handshake; the game answers in kind.</summary>
    Hello = 1,

    /// <summary>Both directions. Ends the debugger session.</summary>
    Goodbye = 2,

    RequestScriptList = 3,

    RequestThreadList = 4,

    AddBreakpoint = 7,

    RemoveBreakpoint = 8,

    BreakAll = 9,

    StepOver = 10,

    StepInto = 11,

    StepOut = 12,

    /// <summary>Sent by the game roughly every five seconds to an idle client. Carries nothing.</summary>
    Heartbeat = 13,

    AttachScript = 14,

    SelectScript = 15,

    /// <summary>Arms a break for one coroutine of the selected script; not a selection.</summary>
    BreakThread = 16,

    Continue = 17,

    DumpVariable = 19,

    DumpTable = 20,

    SetCallstackDepth = 21,

    ScriptList = 24,

    ThreadList = 25,

    ScriptAdded = 27,

    ScriptRemoved = 28,

    ScriptSuspended = 29,

    VariableDump = 30,

    TableDump = 31,

    ChildScriptList = 32,

    Output = 33,

    ExecuteText = 34,

    ExecuteTextResponse = 35
}