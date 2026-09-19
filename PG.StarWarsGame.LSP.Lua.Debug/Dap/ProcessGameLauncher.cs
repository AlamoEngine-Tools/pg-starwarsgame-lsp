// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;

namespace PG.StarWarsGame.LSP.Lua.Debug.Dap;

/// <inheritdoc />
public sealed class ProcessGameLauncher : IGameLauncher
{
    public IGameProcess Start(string program, IReadOnlyList<string> arguments, string? workingDirectory)
    {
        var info = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(Path.GetFullPath(program)) ?? string.Empty
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {program}");
        return new GameProcess(process);
    }

    private sealed class GameProcess(Process process) : IGameProcess
    {
        public int Id => process.Id;

        public bool HasExited => process.HasExited;

        public int? ExitCode => process.HasExited ? process.ExitCode : null;

        public void Kill()
        {
            if (!process.HasExited)
                process.Kill(true);
        }

        public void Dispose()
        {
            process.Dispose();
        }
    }
}