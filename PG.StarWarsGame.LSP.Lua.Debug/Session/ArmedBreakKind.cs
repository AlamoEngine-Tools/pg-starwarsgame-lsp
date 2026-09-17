// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <summary>Which kind of break is armed while the state is <see cref="DebugRunState.BreakArmed" />.</summary>
public enum ArmedBreakKind
{
    None,

    /// <summary>Break at the next Lua line of the context script, or of any attached script when there is none.</summary>
    BreakAll,

    /// <summary>Break at the next Lua line of one coroutine of the context script.</summary>
    BreakThread,

    /// <summary>A step over, into or out of the frame that was suspended.</summary>
    Step
}
