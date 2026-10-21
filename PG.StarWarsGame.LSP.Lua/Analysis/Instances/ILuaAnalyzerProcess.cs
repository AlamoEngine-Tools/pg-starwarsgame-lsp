// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Instances;

/// <summary>What one script instance's analyzer is pointed at.</summary>
/// <param name="Root">The instance's folder: the workspace, holding the instance's files and nothing else.</param>
/// <param name="Library">The stub files it reads for every document: the engine's, and a library's where no layer ships it.</param>
public sealed record LuaAnalyzerInstancePlan(string Root, IReadOnlyList<string> Library);

/// <summary>
///     One analyzer process, for one script instance. Disposed synchronously on the server's own
///     shutdown, which must never leave a process running.
/// </summary>
public interface ILuaAnalyzerProcess : ILuaAnalyzer, IAsyncDisposable, IDisposable
{
    /// <summary>Starts the process for <paramref name="plan" />, or restarts it with every document sent so far.</summary>
    Task StartAsync(LuaAnalyzerInstancePlan plan);
}

/// <summary>Takes the project's layers whenever they are (re)loaded.</summary>
public interface ILuaAnalyzerLayers
{
    void Configure(IReadOnlyList<ProjectLayer> layers);
}
