// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace PG.StarWarsGame.LSP.Lua.Debug.Dap;

/// <summary>The <c>attach</c> request: the game is already running with its debug server up.</summary>
public sealed record LuaAttachArguments : AttachRequestArguments, ILuaSessionSettings
{
    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 1234;

    public string? ClientName { get; init; }

    public IReadOnlyList<string>? SourceRoots { get; init; }

    public bool UnsafeTableExpansion { get; init; }

    public bool DropDuplicateOutermostFrame { get; init; } = true;
}