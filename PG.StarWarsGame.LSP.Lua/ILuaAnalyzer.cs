// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Lua;

/// <summary>
///     The Lua analyzer running behind this server (emmylua_ls as a sidecar): the documents the
///     editor has open, mirrored to it, and what it reports back. Every call is a no-op while the
///     analyzer is not running - turned off, not installed, or between a crash and its restart.
/// </summary>
public interface ILuaAnalyzer
{
    /// <summary>Whether an analyzer process is up and initialized.</summary>
    bool IsRunning { get; }

    /// <summary>Raised for every diagnostics publish of the analyzer, on its reader thread.</summary>
    event Action<PublishDiagnosticsParams>? DiagnosticsPublished;

    void DidOpen(string uri, string text, int version);

    /// <summary>The document's full new text; the mirror is full-sync.</summary>
    void DidChange(string uri, string text, int version);

    void DidClose(string uri);

    /// <summary>
    ///     Asks the analyzer <paramref name="method" /> with <paramref name="parameters" />. Null when
    ///     it is not running, does not answer in time, or fails - the caller answers with its own.
    /// </summary>
    Task<T?> RequestAsync<T>(string method, object parameters, CancellationToken ct) where T : class;
}