// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.LSP.Server;

if (args.Contains("--wait-for-debugger") || Environment.GetEnvironmentVariable("LSP_WAIT_DEBUGGER") == "1")
{
    Console.Error.WriteLine($"[LSP] Waiting for debugger — PID {Environment.ProcessId}");
    while (!Debugger.IsAttached)
        Thread.Sleep(100);
    Console.Error.WriteLine("[LSP] Debugger attached, continuing startup.");
}

var tcpPortArg = args.FirstOrDefault(a => a.StartsWith("--tcp=", StringComparison.Ordinal));
if (tcpPortArg is not null && int.TryParse(tcpPortArg["--tcp=".Length..], out var tcpPort))
{
    var listener = new TcpListener(IPAddress.Loopback, tcpPort);
    listener.Start();
    Console.Error.WriteLine($"[LSP] PID {Environment.ProcessId} listening on TCP port {tcpPort} — connect your LSP client now");

    // Accept one connection at a time; loop so multiple sequential test fixtures can reuse this server process.
    while (true)
    {
        var tcpClient = await listener.AcceptTcpClientAsync();
        Console.Error.WriteLine($"[LSP] Client connected from {tcpClient.Client.RemoteEndPoint}");
        var stream = tcpClient.GetStream();
        var server = await LanguageServer.From(options =>
            ServerConfigurator.Apply(options
                .WithInput(stream)
                .WithOutput(stream)));
        await server.WaitForExit;
        Console.Error.WriteLine("[LSP] Client disconnected — waiting for next connection");
    }
}
else
{
    var server = await LanguageServer.From(options =>
        ServerConfigurator.Apply(options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput())));

    await server.WaitForExit;
}