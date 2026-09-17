// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Dap;

/// <summary>
///     The custom requests and the event behind the Lua scripts view. DAP has no place for a
///     list of live script instances with their coroutine threads, so the view asks for it here.
/// </summary>
public static class LuaCustomMessages
{
    /// <summary>Request: the current script list as the adapter knows it, without touching the game.</summary>
    public const string Scripts = "eawLua/scripts";

    /// <summary>Request: reload the script list from the game, or one script's threads (<see cref="LuaRefreshArguments" />).</summary>
    public const string Refresh = "eawLua/refresh";

    /// <summary>Request: arm a break at the next Lua line of one script.</summary>
    public const string SelectScript = "eawLua/selectScript";

    /// <summary>Request: arm a break at the next Lua line of one coroutine thread of a script.</summary>
    public const string BreakThread = "eawLua/breakThread";

    /// <summary>Event: the script list or the run state changed; the view re-requests <see cref="Scripts" />.</summary>
    public const string ScriptsChanged = "eawLua/scriptsChanged";
}

/// <summary>The run state names on the wire.</summary>
public static class LuaRunStates
{
    public const string Running = "running";

    public const string BreakArmed = "breakArmed";

    public const string Suspended = "suspended";
}

/// <summary>Why a <see cref="LuaScriptsChangedEvent" /> was sent.</summary>
public static class LuaScriptsChangedReasons
{
    public const string ScriptAdded = "scriptAdded";

    public const string ScriptRemoved = "scriptRemoved";

    public const string Suspended = "suspended";

    public const string StateChanged = "stateChanged";
}

/// <summary>Arguments of <see cref="LuaCustomMessages.Refresh" />: a script id reloads that script's threads, none reloads the list.</summary>
public sealed record LuaRefreshArguments
{
    public int? ScriptId { get; init; }
}

/// <summary>Arguments of <see cref="LuaCustomMessages.SelectScript" />.</summary>
public sealed record LuaSelectScriptArguments
{
    public int ScriptId { get; init; }
}

/// <summary>Arguments of <see cref="LuaCustomMessages.BreakThread" />; thread index -1 is the script's main state.</summary>
public sealed record LuaBreakThreadArguments
{
    public int ScriptId { get; init; }

    public int ThreadIndex { get; init; } = -1;
}

/// <summary>What every scripts-view request answers with: the run state and one row per live script instance.</summary>
public sealed record LuaScriptsResult(string RunState, IReadOnlyList<LuaScriptRow> Scripts);

/// <summary>
///     One live script instance. <paramref name="Path" /> is the workspace file the game path
///     resolves to, or null when it is under none of the source roots. <paramref name="Threads" />
///     is null until the game has been asked for that script's threads.
/// </summary>
public sealed record LuaScriptRow(
    int ScriptId,
    string GamePath,
    string Name,
    string? Path,
    bool Attached,
    bool IsContext,
    bool IsSuspended,
    IReadOnlyList<LuaThreadRow>? Threads);

/// <summary>A named coroutine thread of a script.</summary>
public sealed record LuaThreadRow(int ThreadIndex, string Name);

/// <summary>Body of the <see cref="LuaCustomMessages.ScriptsChanged" /> event.</summary>
public sealed record LuaScriptsChangedEvent(string Reason, int? ScriptId);
