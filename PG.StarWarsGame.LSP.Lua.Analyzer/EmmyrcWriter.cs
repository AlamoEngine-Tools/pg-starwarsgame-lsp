// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Lua.Analyzer;

/// <summary>
///     The plan of the whole-project analyzer: the leaf's scripts as its workspace, the lower layers and
///     every stub as its library. It answers navigation that crosses states - a rename, and what a
///     library file is used for downstream - and nothing else.
/// </summary>
public static class EmmyrcWriter
{
    /// <summary>
    ///     2 EaW and 6 FoC scripts are Windows-1252, and the default UTF-8 drops them without a word
    ///     (measured). One encoding per configuration, so the one every vanilla file reads in.
    /// </summary>
    public const string Encoding = "windows-1252";

    /// <param name="layers">The project's layers; the highest rank is the leaf.</param>
    /// <param name="stubDirectory">The directory holding the engine's ---@meta stubs, when known.</param>
    public static AnalyzerPlan Plan(IReadOnlyList<ProjectLayer> layers, string? stubDirectory)
    {
        if (layers.Count == 0) return new AnalyzerPlan([], [], Encoding);

        var leafRank = layers.Max(l => l.Rank);
        var workspace = layers.Where(l => l.Rank == leafRank).SelectMany(l => l.ScriptRoots).Distinct().ToList();
        var library = layers.Where(l => l.Rank < leafRank)
            .OrderByDescending(l => l.Rank)
            .SelectMany(l => l.ScriptRoots)
            .Where(root => !workspace.Contains(root))
            .Distinct()
            .ToList();
        if (stubDirectory is not null) library.Add(stubDirectory);
        return new AnalyzerPlan(workspace, library, Encoding);
    }
}
