// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;

namespace PG.StarWarsGame.LSP.Core.Configuration;

/// <summary>
///     Runtime timeout and debounce settings for the LSP server.
///     Defaults are suitable for normal operation; use <see cref="WithDebugger" /> to disable all
///     timeouts when the process is launched under a debugger.
/// </summary>
public sealed record ServerOptions
{
    public static readonly ServerOptions Default = new();

    /// <summary>Maximum time to wait for the schema provider to become ready before proceeding with a degraded scan.</summary>
    public TimeSpan SchemaWaitTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum time to wait for the LSP client to acknowledge a window/workDoneProgress/create request.</summary>
    public TimeSpan ProgressReporterTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Debounce window for diagnostics re-publishing after an index change.</summary>
    public TimeSpan DiagnosticsDebounce { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    ///     Returns a copy with all wait timeouts set to infinite and the debounce set to zero.
    ///     Call this when launching with <c>--wait-for-debugger</c> so breakpoints and slow attach
    ///     do not trigger premature cancellation.
    /// </summary>
    public ServerOptions WithDebugger()
    {
        return this with
        {
            SchemaWaitTimeout = Timeout.InfiniteTimeSpan,
            ProgressReporterTimeout = Timeout.InfiniteTimeSpan,
            DiagnosticsDebounce = TimeSpan.Zero
        };
    }

    /// <summary>
    ///     Parses a JSON string (typically the contents of <c>appsettings.json</c>) and returns
    ///     a <see cref="ServerOptions" /> that overrides only the values present in the JSON.
    ///     Missing keys retain their defaults.
    /// </summary>
    public static ServerOptions FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("server", out var server))
            return Default;

        var result = Default;

        if (server.TryGetProperty("schemaWaitTimeoutSeconds", out var swt) &&
            swt.TryGetDouble(out var swts))
            result = result with { SchemaWaitTimeout = TimeSpan.FromSeconds(swts) };

        if (server.TryGetProperty("progressReporterTimeoutSeconds", out var prt) &&
            prt.TryGetDouble(out var prts))
            result = result with { ProgressReporterTimeout = TimeSpan.FromSeconds(prts) };

        if (server.TryGetProperty("diagnosticsDebounceMs", out var ddb) &&
            ddb.TryGetDouble(out var ddbs))
            result = result with { DiagnosticsDebounce = TimeSpan.FromMilliseconds(ddbs) };

        return result;
    }
}