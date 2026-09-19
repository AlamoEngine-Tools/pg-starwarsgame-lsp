// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Dap;

/// <summary>Starts the game for a <c>launch</c> request. Abstracted so tests never spawn anything.</summary>
public interface IGameLauncher
{
    IGameProcess Start(string program, IReadOnlyList<string> arguments, string? workingDirectory);
}

/// <summary>A started game process.</summary>
public interface IGameProcess : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    int? ExitCode { get; }

    void Kill();
}