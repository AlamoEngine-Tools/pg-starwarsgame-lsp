// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.LSP.Server;

if (args.Contains("--wait-for-debugger") || Environment.GetEnvironmentVariable("LSP_WAIT_DEBUGGER") == "1")
{
    Console.Error.WriteLine($"[LSP] Waiting for debugger — PID {Environment.ProcessId}");
    while (!Debugger.IsAttached)
        Thread.Sleep(100);
    Console.Error.WriteLine("[LSP] Debugger attached, continuing startup.");
}

var server = await LanguageServer.From(options =>
    ServerConfigurator.Apply(options
        .WithInput(Console.OpenStandardInput())
        .WithOutput(Console.OpenStandardOutput())));

await server.WaitForExit;