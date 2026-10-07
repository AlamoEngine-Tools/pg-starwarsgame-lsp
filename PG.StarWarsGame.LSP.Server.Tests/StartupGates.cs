// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>Real startup gates for notifier tests: one already open, one still buffering.</summary>
internal static class StartupGates
{
    public static StartupGate Open()
    {
        var gate = new StartupGate();
        gate.OpenAsync().GetAwaiter().GetResult();
        return gate;
    }

    public static StartupGate Closed()
    {
        return new StartupGate();
    }
}
