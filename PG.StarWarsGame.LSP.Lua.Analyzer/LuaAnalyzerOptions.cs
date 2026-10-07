// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Analyzer;

/// <summary>How the analyzer process is started and supervised.</summary>
public sealed record LuaAnalyzerOptions
{
    /// <summary>
    ///     The arguments emmylua_ls runs with: the game opens a stdlib of its own (Lua 5.0.2), so the
    ///     analyzer's 5.x stdlib stays out and the stubs stand in for it.
    /// </summary>
    public static readonly IReadOnlyList<string> AnalyzerArguments = ["--load-stdlib", "false", "--editor", "vscode"];

    /// <summary>The executable: emmylua_ls itself, or a host for it (tests start a stand-in with 'dotnet').</summary>
    public required string Command { get; init; }

    /// <summary>Arguments before <see cref="AnalyzerArguments" />, such as the stand-in's dll.</summary>
    public IReadOnlyList<string> LeadingArguments { get; init; } = [];

    /// <summary>Where the configuration file is written; one directory per server process.</summary>
    public required string ConfigDirectory { get; init; }

    /// <summary>How long a request may take before it is given up and answered with nothing.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long initialize may take before the start counts as failed.</summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The first restart delay after a crash; each crash in a row doubles it.</summary>
    public TimeSpan RestartDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest restart delay.</summary>
    public TimeSpan MaxRestartDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>A process that ran this long before dying resets the doubling.</summary>
    public TimeSpan StableAfter { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Crashes in a row after which the analyzer stays off for the session.</summary>
    public int MaxConsecutiveCrashes { get; init; } = 6;

    /// <summary>Extra environment for the process.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}
