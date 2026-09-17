// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using PG.StarWarsGame.LSP.Lua.Debug.Protocol;

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <summary>
///     A debugging session with one game: the connection plus a mirror of the game's debugger
///     state. Every command is checked against that mirror first and refused with
///     <see cref="LuaDebugStateException" /> when the game would assert on it; only then does it
///     go on the wire. Requests that the game answers are correlated with their reply and time out
///     after <see cref="RequestTimeout" />. Every live script instance is tracked by id from the
///     script list and the added/removed notifications.
/// </summary>
public interface ILuaDebugSession : IAsyncDisposable
{
    bool IsConnected { get; }

    DebugRunState RunState { get; }

    ArmedBreakKind ArmedKind { get; }

    /// <summary>The script the game's break, step and thread commands apply to; set by selection or by a suspension.</summary>
    int? ContextScriptId { get; }

    /// <summary>The script that is stopped, while <see cref="RunState" /> is <see cref="DebugRunState.Suspended" />.</summary>
    int? SuspendedScriptId { get; }

    /// <summary>The stopped script's frames as the game sent them, outermost first.</summary>
    IReadOnlyList<string> Callstack { get; }

    /// <summary>The frame currently selected for variable reads, as an index into <see cref="Callstack" />.</summary>
    int? SelectedFrame { get; }

    /// <summary>Every live script instance by id, from the last list plus added/removed notifications.</summary>
    IReadOnlyDictionary<int, ScriptEntry> Scripts { get; }

    IReadOnlyCollection<int> AttachedScriptIds { get; }

    /// <summary>The named coroutine threads last reported per script id.</summary>
    IReadOnlyDictionary<int, IReadOnlyList<ThreadEntry>> ThreadsByScript { get; }

    TimeSpan RequestTimeout { get; set; }

    /// <summary>A script stopped. Raised after the session's own state has been updated.</summary>
    event Action<ScriptSuspendedMessage>? Suspended;

    event Action<ScriptEntry>? ScriptAdded;

    event Action<int>? ScriptRemoved;

    event Action<OutputMessage>? Output;

    /// <summary>The game is gone; every pending request has already failed.</summary>
    event Action<Exception>? ConnectionLost;

    /// <summary>Connects, completes the debugger hello, and loads the first script list.</summary>
    Task ConnectAsync(IPEndPoint remote, string clientName, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScriptEntry>> RequestScriptsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ThreadEntry>> RequestThreadsAsync(int scriptId, CancellationToken cancellationToken);

    /// <summary>Installs the game's line hook on a script; returns the sources loaded under it.</summary>
    Task<IReadOnlyList<string>> AttachAsync(int scriptId, CancellationToken cancellationToken);

    /// <summary>
    ///     Script id -1 with thread id -1 is a source-wide breakpoint. A script-scoped breakpoint
    ///     attaches the script first when it is not attached yet.
    /// </summary>
    Task AddBreakpointAsync(int scriptId, int threadId, string sourceName, int line,
        CancellationToken cancellationToken);

    Task RemoveBreakpointAsync(int scriptId, int threadId, string sourceName, int line,
        CancellationToken cancellationToken);

    /// <summary>Arms a break at the next Lua line of the context script, or of any attached script when none is selected.</summary>
    Task BreakAllAsync(CancellationToken cancellationToken);

    /// <summary>Makes a script the context and arms a break at its next Lua line.</summary>
    Task SelectScriptAsync(int scriptId, CancellationToken cancellationToken);

    /// <summary>Arms a break at the next Lua line of one coroutine of the context script; -1 is its main state.</summary>
    Task BreakThreadAsync(int threadId, CancellationToken cancellationToken);

    Task ContinueAsync(CancellationToken cancellationToken);

    Task StepOverAsync(CancellationToken cancellationToken);

    Task StepIntoAsync(CancellationToken cancellationToken);

    Task StepOutAsync(CancellationToken cancellationToken);

    /// <summary>Selects the frame later variable reads resolve locals against; the index is into <see cref="Callstack" />.</summary>
    Task SelectFrameAsync(int level, CancellationToken cancellationToken);

    Task<VariableDumpMessage> DumpVariableAsync(int scriptId, string variableName, CancellationToken cancellationToken);

    Task<TableDumpMessage> DumpTableAsync(int scriptId, string tableName, IReadOnlyList<uint> path,
        CancellationToken cancellationToken);

    /// <summary>Runs a Lua chunk in the script's main state and returns the game's text reply.</summary>
    Task<string> ExecuteTextAsync(int scriptId, string text, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}