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

    public static string? GamePath =>
        Environment.GetEnvironmentVariable("LSP_GAME_PATH");

    public static string? BaselineLocalPath =>
        Environment.GetEnvironmentVariable("LSP_BASELINE_LOCAL_PATH");

    public static string Locale =>
        Environment.GetEnvironmentVariable("LSP_LOCALE") ?? "en";

    public static IReadOnlyList<string> ModPaths =>
        (Environment.GetEnvironmentVariable("LSP_MOD_PATHS") ?? string.Empty)
        .Split(';', StringSplitOptions.RemoveEmptyEntries);

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

    private static string? ExistingPath(string? path) =>
        path is not null && Directory.Exists(path) ? path : null;

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
