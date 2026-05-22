// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.E2E.Tests;

public static class LspTestEnvironment
{
    public static string? SchemaLocalPath =>
        Environment.GetEnvironmentVariable("LSP_SCHEMA_LOCAL_PATH");

    public static string? GamePath =>
        Environment.GetEnvironmentVariable("LSP_GAME_PATH");

    public static string? WorkspacePath =>
        Environment.GetEnvironmentVariable("LSP_WORKSPACE_PATH");

    public static string? BaselineLocalPath =>
        Environment.GetEnvironmentVariable("LSP_BASELINE_LOCAL_PATH");

    public static string Locale =>
        Environment.GetEnvironmentVariable("LSP_LOCALE") ?? "en";

    public static IReadOnlyList<string> ModPaths =>
        (Environment.GetEnvironmentVariable("LSP_MOD_PATHS") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// When set, the fixture connects to an already-running server on this TCP port
    /// instead of spawning a child process. Useful for attaching a debugger.
    /// Start the server with: dotnet run -- --tcp=&lt;port&gt; [--wait-for-debugger]
    /// </summary>
    public static int? ExternalServerPort =>
        Environment.GetEnvironmentVariable("LSP_SERVER_TCP_PORT") is { } val
        && int.TryParse(val, out var port)
            ? port
            : null;
}
