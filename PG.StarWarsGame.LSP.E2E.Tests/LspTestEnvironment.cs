// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.E2E.Tests;

public static class LspTestEnvironment
{
    // Walk up from the test assembly to find the solution root (directory containing *.slnx).
    private static readonly string? SolutionRoot = FindSolutionRoot(
        Path.GetDirectoryName(typeof(LspTestEnvironment).Assembly.Location)!);

    /// <summary>
    ///     Schema directory.  Prefers <c>LSP_SCHEMA_LOCAL_PATH</c> env var; falls back to
    ///     <c>schema/eaw</c> inside the repository.  Returns <c>null</c> if neither exists.
    /// </summary>
    public static string? SchemaLocalPath =>
        ExistingPath(Environment.GetEnvironmentVariable("LSP_SCHEMA_LOCAL_PATH"))
        ?? ExistingPath(SolutionRoot is not null ? Path.Combine(SolutionRoot, "schema", "eaw") : null);

    /// <summary>
    ///     Workspace root.  Prefers <c>LSP_WORKSPACE_PATH</c> env var; falls back to
    ///     <c>foc/</c> inside the repository.  Returns <c>null</c> if neither exists.
    /// </summary>
    public static string? WorkspacePath =>
        ExistingPath(Environment.GetEnvironmentVariable("LSP_WORKSPACE_PATH"))
        ?? ExistingPath(SolutionRoot is not null ? Path.Combine(SolutionRoot, "foc") : null);

    /// <summary>
    ///     EaW workspace root.  Prefers <c>LSP_EAW_WORKSPACE_PATH</c> env var; falls back to
    ///     <c>eaw/</c> inside the repository.  Returns <c>null</c> if neither exists.
    /// </summary>
    public static string? EawWorkspacePath =>
        ExistingPath(Environment.GetEnvironmentVariable("LSP_EAW_WORKSPACE_PATH"))
        ?? ExistingPath(SolutionRoot is not null ? Path.Combine(SolutionRoot, "eaw") : null);

    /// <summary>
    ///     The suite's own authored mod workspace. Prefers <c>LSP_E2E_WORKSPACE_PATH</c>; falls back to
    ///     <c>e2e-workspace/</c> inside the repository. Unlike <c>eaw/</c> and <c>foc/</c> this one is
    ///     TRACKED, because nothing in it comes from the game - which is what lets a test edit it to
    ///     create the condition it needs.
    /// </summary>
    public static string? E2eWorkspacePath =>
        ExistingPath(Environment.GetEnvironmentVariable("LSP_E2E_WORKSPACE_PATH"))
        ?? ExistingPath(SolutionRoot is not null ? Path.Combine(SolutionRoot, "e2e-workspace") : null);

    public static string? GamePath =>
        Environment.GetEnvironmentVariable("LSP_GAME_PATH");

    /// <summary>
    ///     Shipped-game baseline index. Prefers <c>LSP_BASELINE_LOCAL_PATH</c>; falls back to the
    ///     FoC baseline checked in under <c>baseline/</c>, matching the <c>foc/</c> workspace the
    ///     fixtures open by default. Returns <c>null</c> if neither exists.
    /// </summary>
    public static string? BaselineLocalPath =>
        ExistingFile(Environment.GetEnvironmentVariable("LSP_BASELINE_LOCAL_PATH"))
        ?? ExistingFile(SolutionRoot is not null
            ? Path.Combine(SolutionRoot, "baseline", "foc", "aet-pg-swg-lsp-foc-baseline.aet")
            : null);

    public static string Locale =>
        Environment.GetEnvironmentVariable("LSP_LOCALE") ?? "en";

    /// <summary>
    ///     When set, the fixture connects to an already-running server on this TCP port
    ///     instead of spawning a child process. Useful for attaching a debugger.
    ///     Start the server with: dotnet run -- --tcp=&lt;port&gt; [--wait-for-debugger]
    /// </summary>
    public static int? ExternalServerPort =>
        Environment.GetEnvironmentVariable("LSP_SERVER_TCP_PORT") is { } val
        && int.TryParse(val, out var port)
            ? port
            : null;

    private static string? ExistingPath(string? path)
    {
        return path is not null && Directory.Exists(path) ? path : null;
    }

    // The baseline is a FILE, not a directory - ExistingPath would reject it whatever it points at.
    private static string? ExistingFile(string? path)
    {
        return path is not null && File.Exists(path) ? path : null;
    }

    private static string? FindSolutionRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }
}