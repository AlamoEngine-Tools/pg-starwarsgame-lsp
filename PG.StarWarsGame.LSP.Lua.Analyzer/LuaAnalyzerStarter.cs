// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis.Instances;

namespace PG.StarWarsGame.LSP.Lua.Analyzer;

/// <summary>Starts, or restarts, the Lua analyzer for the project's layers.</summary>
public interface ILuaAnalyzerStarter
{
    /// <summary>Called whenever the project's layers are (re)loaded.</summary>
    Task ConfigureAsync(IReadOnlyList<ProjectLayer> layers);
}

/// <summary>
///     Hands the layers to the analyzer when <c>features.lua.analyzer</c> is on and its executable was
///     found: from then on every open script gets an analyzer of its own, resolved over them.
/// </summary>
public sealed class LuaAnalyzerStarter(
    ILuaAnalyzerLayers analyzer,
    ILspConfigurationProvider config,
    bool executableFound,
    ILogger<LuaAnalyzerStarter> logger) : ILuaAnalyzerStarter
{
    public Task ConfigureAsync(IReadOnlyList<ProjectLayer> layers)
    {
        if (!config.Current.Features.Lua.Analyzer)
        {
            logger.LogDebug("Lua analyzer off (features.lua.analyzer)");
            return Task.CompletedTask;
        }

        if (!executableFound)
        {
            logger.LogInformation(
                "Lua analyzer not started: emmylua_ls not found (set {Variable}, or place it in the server's emmylua folder)",
                LuaAnalyzerExecutable.Variable);
            return Task.CompletedTask;
        }

        analyzer.Configure(layers);
        return Task.CompletedTask;
    }
}

/// <summary>Where the emmylua_ls executable is.</summary>
public static class LuaAnalyzerExecutable
{
    /// <summary>An explicit path to the executable; wins over the default location.</summary>
    public const string Variable = "AET_EMMYLUA_LS";

    /// <summary>The variable, then <c>emmylua/emmylua_ls</c> beside the server; null when neither exists.</summary>
    public static string? Resolve(string serverDirectory, Func<string, string?> environment, Func<string, bool> exists)
    {
        if (environment(Variable) is { Length: > 0 } explicitPath && exists(explicitPath)) return explicitPath;
        var beside = Path.Combine(serverDirectory, "emmylua",
            OperatingSystem.IsWindows() ? "emmylua_ls.exe" : "emmylua_ls");
        return exists(beside) ? beside : null;
    }
}