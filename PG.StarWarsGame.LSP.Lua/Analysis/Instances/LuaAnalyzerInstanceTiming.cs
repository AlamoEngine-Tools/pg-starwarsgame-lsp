// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Analysis.Instances;

/// <summary>
///     When a script's analyzer starts and stops around the editor opening and closing the script.
/// </summary>
/// <param name="Settle">
///     How long a script stays open before its analyzer starts. The editor opens a file for a moment
///     to show a hover or a peek of it and closes it again; an analyzer started for that was killed
///     while it started.
/// </param>
/// <param name="Grace">How long a closed script's analyzer is kept, ready for the script reopened.</param>
public sealed record LuaAnalyzerInstanceTiming(TimeSpan Settle, TimeSpan Grace)
{
    /// <summary>An analyzer from the open to the close of its script, no longer.</summary>
    public static LuaAnalyzerInstanceTiming Immediate { get; } = new(TimeSpan.Zero, TimeSpan.Zero);

    /// <summary>The editor's: a quarter second to settle, a minute's grace.</summary>
    public static LuaAnalyzerInstanceTiming Editor { get; } =
        new(TimeSpan.FromMilliseconds(250), TimeSpan.FromMinutes(1));
}