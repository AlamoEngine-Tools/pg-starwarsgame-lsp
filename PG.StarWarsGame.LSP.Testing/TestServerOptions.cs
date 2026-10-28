// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>The server options a test runs under.</summary>
public static class TestServerOptions
{
    /// <summary>
    ///     No debounce: every index change publishes at once, so a test reads the result on the
    ///     next line instead of waiting for a timer.
    /// </summary>
    public static ServerOptions Immediate { get; } = ServerOptions.Default with { DiagnosticsDebounce = TimeSpan.Zero };
}