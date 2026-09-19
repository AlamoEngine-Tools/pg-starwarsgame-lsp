// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Dap;

/// <summary>What both attach and launch configurations say about the debugger session itself.</summary>
public interface ILuaSessionSettings
{
    /// <summary>The machine running the game; the default is this one.</summary>
    string Host { get; }

    /// <summary>The game's Lua debug UDP port; the game takes the first free one from 1234 upward.</summary>
    int Port { get; }

    /// <summary>The name the game logs for this client; defaults to the adapter's name and process id.</summary>
    string? ClientName { get; }

    /// <summary>Script root directories, mod first, then its dependencies and the game.</summary>
    IReadOnlyList<string>? SourceRoots { get; }

    /// <summary>
    ///     Allow expanding table values. Off by default: the game is reported to assert on any
    ///     member whose display text is 255 bytes or longer, which loses the game session.
    /// </summary>
    bool UnsafeTableExpansion { get; }

    /// <summary>Drop the outermost call-stack entry when it duplicates the one above it.</summary>
    bool DropDuplicateOutermostFrame { get; }
}