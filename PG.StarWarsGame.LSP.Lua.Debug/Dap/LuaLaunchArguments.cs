// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace PG.StarWarsGame.LSP.Lua.Debug.Dap;

/// <summary>
///     The <c>launch</c> request: start the game executable, then attach as soon as its debug
///     server answers. The arguments are passed verbatim; the mod chain is the client's to build.
/// </summary>
public sealed record LuaLaunchArguments : LaunchRequestArguments, ILuaSessionSettings
{
    /// <summary>The internal/debug game executable.</summary>
    public string Program { get; init; } = string.Empty;

    public IReadOnlyList<string>? Args { get; init; }

    /// <summary>Working directory; defaults to the executable's directory.</summary>
    public string? Cwd { get; init; }

    /// <summary>How long to keep trying to attach after the process starts.</summary>
    public int AttachTimeoutSeconds { get; init; } = 60;

    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 1234;

    public string? ClientName { get; init; }

    public IReadOnlyList<string>? SourceRoots { get; init; }

    public bool UnsafeTableExpansion { get; init; }

    public bool DropDuplicateOutermostFrame { get; init; } = true;
}