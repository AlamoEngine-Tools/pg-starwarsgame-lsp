// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Server;
using Serilog;

Console.Error.WriteLine(
    $"[LSP] PID {Environment.ProcessId} args=[{(args.Length == 0 ? "<none>" : string.Join(", ", args))}]");

var waitForDebugger = args.Contains("--wait-for-debugger") ||
                      Environment.GetEnvironmentVariable("LSP_WAIT_DEBUGGER") == "1";

if (waitForDebugger)
{
    Console.Error.WriteLine($"[LSP] Waiting for debugger — PID {Environment.ProcessId}");
    while (!Debugger.IsAttached)
        Thread.Sleep(100);
    Console.Error.WriteLine("[LSP] Debugger attached, continuing startup.");
}

#if DEBUG
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.File("aetswg-.log", rollingInterval: RollingInterval.Day)
    .MinimumLevel.Debug()
    .CreateLogger();
#endif

var serverOptions = LoadServerOptions();
if (waitForDebugger)
    serverOptions = serverOptions.WithDebugger();

var tcpPortArg = args.FirstOrDefault(a => a.StartsWith("--tcp=", StringComparison.Ordinal));
if (tcpPortArg is not null && int.TryParse(tcpPortArg["--tcp=".Length..], out var tcpPort))
{
    var listener = new TcpListener(IPAddress.Loopback, tcpPort);
    listener.Start();
    Console.Error.WriteLine(
        $"[LSP] PID {Environment.ProcessId} listening on TCP port {tcpPort} — connect your LSP client now");

    // Accept one connection at a time; loop so multiple sequential test fixtures can reuse this server process.
    while (true)
    {
        var tcpClient = await listener.AcceptTcpClientAsync();
        Console.Error.WriteLine($"[LSP] Client connected from {tcpClient.Client.RemoteEndPoint}");
        var stream = tcpClient.GetStream();
        var server = await LanguageServer.From(options =>
            ServerConfigurator.Apply(options
                .WithInput(stream)
                .WithOutput(stream), serverOptions));
        await server.WaitForExit;
        Console.Error.WriteLine("[LSP] Client disconnected — waiting for next connection");
    }
}

{
    var server = await LanguageServer.From(options =>
        ServerConfigurator.Apply(options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput()), serverOptions));

    await server.WaitForExit;
}

static ServerOptions LoadServerOptions()
{
    var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    if (!File.Exists(settingsPath))
        return ServerOptions.Default;

    try
    {
        return ServerOptions.FromJson(File.ReadAllText(settingsPath));
    }
    catch
    {
        return ServerOptions.Default;
    }
}