// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Lua.Analyzer;

/// <summary>What the analyzer is pointed at: the workspace it reports on, and what it only reads.</summary>
/// <param name="WorkspaceRoots">The leaf project's script roots - the files the author edits.</param>
/// <param name="Library">Lower layers' script roots, then the engine stubs: read for types, never reported on.</param>
public sealed record AnalyzerPlan(IReadOnlyList<string> WorkspaceRoots, IReadOnlyList<string> Library)
{
    /// <summary>
    ///     Rules the analyzer never runs. Global scope is this server's: the analyzer sees one
    ///     namespace where the game gives every script its own, so its undefined-global is a subset
    ///     of ours (measured) and the rest would be wrong. require resolution is ours too - the
    ///     engine folds case and searches the layers, the analyzer does neither.
    /// </summary>
    public static readonly IReadOnlyList<string> DisabledRules =
    [
        "undefined-global",
        "global-in-non-module",
        "unresolved-require",
        "duplicate-require"
    ];

    /// <summary>
    ///     2 EaW and 6 FoC scripts are Windows-1252, and the default UTF-8 drops them without a
    ///     word (measured). One encoding per configuration, so the one every vanilla file reads in.
    /// </summary>
    public const string Encoding = "windows-1252";

    /// <summary>The configuration file emmylua_ls reads through EMMYLUALS_CONFIG.</summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(new
        {
            runtime = new { version = "Lua5.1" }, // the engine is 5.0.2, which nothing models; 5.1 plus stubs
            workspace = new { library = Library, encoding = Encoding },
            diagnostics = new { disable = DisabledRules }
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}

/// <summary>Builds the analyzer's plan from the project's layers.</summary>
public static class EmmyrcWriter
{
    /// <param name="layers">The project's layers; the highest rank is the leaf.</param>
    /// <param name="stubDirectory">The directory holding the engine's ---@meta stubs, when known.</param>
    public static AnalyzerPlan Plan(IReadOnlyList<ProjectLayer> layers, string? stubDirectory)
    {
        if (layers.Count == 0) return new AnalyzerPlan([], []);

        var leafRank = layers.Max(l => l.Rank);
        var workspace = layers.Where(l => l.Rank == leafRank).SelectMany(l => l.ScriptRoots).Distinct().ToList();
        var library = layers.Where(l => l.Rank < leafRank)
            .OrderByDescending(l => l.Rank)
            .SelectMany(l => l.ScriptRoots)
            .Where(root => !workspace.Contains(root))
            .Distinct()
            .ToList();
        if (stubDirectory is not null) library.Add(stubDirectory);
        return new AnalyzerPlan(workspace, library);
    }
}
