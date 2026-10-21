// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;

namespace PG.StarWarsGame.LSP.Lua.Analyzer;

/// <summary>What the analyzer is pointed at: the workspace it reports on, and what it only reads.</summary>
/// <param name="WorkspaceRoots">
///     One script instance's folder, holding the instance's files and nothing else.
/// </param>
/// <param name="Library">The stub files: read for types, never reported on.</param>
public sealed record AnalyzerPlan(IReadOnlyList<string> WorkspaceRoots, IReadOnlyList<string> Library)
{
    /// <summary>
    ///     Rules the analyzer never runs. Global scope is this server's: it knows the task forces a
    ///     plan maps and the entry points the engine calls, which no stub declares. require resolution
    ///     is ours too - the engine folds case and searches the layers, the analyzer does neither.
    /// </summary>
    public static readonly IReadOnlyList<string> DisabledRules =
    [
        "undefined-global",
        "global-in-non-module",
        "unresolved-require",
        "duplicate-require"
    ];

    /// <summary>The configuration file emmylua_ls reads through EMMYLUALS_CONFIG.</summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(new
        {
            runtime = new { version = "Lua5.1" }, // the engine is 5.0.2, which nothing models; 5.1 plus stubs
            workspace = new { library = Library },
            diagnostics = new { disable = DisabledRules }
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}