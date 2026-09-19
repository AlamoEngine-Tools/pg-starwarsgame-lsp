// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Debug.Dap;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.Dap;

/// <summary>Records launches instead of starting anything; the "process" exits only when a test says so.</summary>
internal sealed class FakeGameLauncher : IGameLauncher
{
    public List<(string Program, IReadOnlyList<string> Arguments, string? WorkingDirectory)> Launches { get; } = [];

    public FakeGameProcess? LastProcess { get; private set; }

    /// <summary>When set, the next started process is already exited with this code.</summary>
    public int? ExitImmediatelyWith { get; set; }

    public IGameProcess Start(string program, IReadOnlyList<string> arguments, string? workingDirectory)
    {
        Launches.Add((program, arguments, workingDirectory));
        LastProcess = new FakeGameProcess(Launches.Count, ExitImmediatelyWith);
        return LastProcess;
    }

    internal sealed class FakeGameProcess(int id, int? exitCode) : IGameProcess
    {
        public int Id { get; } = id;

        public bool HasExited => ExitCode is not null;

        public int? ExitCode { get; private set; } = exitCode;

        public bool Killed { get; private set; }

        public bool Disposed { get; private set; }

        public void Kill()
        {
            Killed = true;
            ExitCode ??= -1;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}