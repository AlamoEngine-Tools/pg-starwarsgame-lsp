// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Lua.Debug.Dap;
using PG.StarWarsGame.LSP.Server;
using PG.StarWarsGame.LSP.Server.Startup;
using Serilog;

// The home directory is written as ~ in what goes to the error stream: the client shows these lines
// in the project's output channel, and the log folder is a path under the home directory.
Console.Error.WriteLine(HomePath.AsTilde(
    $"[LSP] PID {Environment.ProcessId} args=[{(args.Length == 0 ? "<none>" : string.Join(", ", args))}]"));

var waitForDebugger = args.Contains("--wait-for-debugger") ||
                      Environment.GetEnvironmentVariable("LSP_WAIT_DEBUGGER") == "1";

if (waitForDebugger)
{
    Console.Error.WriteLine($"[LSP] Waiting for debugger - PID {Environment.ProcessId}");
    while (!Debugger.IsAttached)
        Thread.Sleep(100);
    Console.Error.WriteLine("[LSP] Debugger attached, continuing startup.");
}

var logLevel = ServerLogLevel.Resolve(args);

// The file log: at the level --log-file-level= asks for in any build, and unasked in a Debug build
// alone. See ServerFileLog.
#if DEBUG
const bool debugBuild = true;
#else
const bool debugBuild = false;
#endif
var fileLogLevel = ServerFileLog.Resolve(args, debugBuild);
if (fileLogLevel is { } fileLevel)
{
    // An absolute path, never a relative one: see ServerLogPath for what a relative one cost.
    var logPath = ServerLogPath.Resolve(args, AppContext.BaseDirectory);
    Console.Error.WriteLine(HomePath.AsTilde($"[LSP] Logging to {logPath}"));
    Log.Logger = ServerFileLog.Create(logPath, fileLevel);
}

// The Lua debug adapter is a second process of this same exe: VS Code starts one per debug
// session and talks DAP to it over stdio. It shares nothing with a running language server.
if (args.Contains("--debug-adapter"))
{
    using var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.SetMinimumLevel(LogLevel.Information);
        ServerFileLog.Apply(builder, fileLogLevel);
    });
    Console.Error.WriteLine($"[DAP] PID {Environment.ProcessId} serving the Lua debug adapter on stdio");
    await LuaDebugAdapterHost.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), loggerFactory,
        CancellationToken.None);
    return;
}

var serverOptions = LoadServerOptions();
if (waitForDebugger)
    serverOptions = serverOptions.WithDebugger();

var tcpPortArg = args.FirstOrDefault(a => a.StartsWith("--tcp=", StringComparison.Ordinal));
if (tcpPortArg is not null && int.TryParse(tcpPortArg["--tcp=".Length..], out var tcpPort))
{
    var listener = new TcpListener(IPAddress.Loopback, tcpPort);
    listener.Start();
    Console.Error.WriteLine(
        $"[LSP] PID {Environment.ProcessId} listening on TCP port {tcpPort} - connect your LSP client now");

    // Accept one connection at a time; loop so multiple sequential test fixtures can reuse this server process.
    while (true)
    {
        var tcpClient = await listener.AcceptTcpClientAsync();
        Console.Error.WriteLine($"[LSP] Client connected from {tcpClient.Client.RemoteEndPoint}");
        var stream = tcpClient.GetStream();
        var server = await LanguageServer.From(options =>
            ServerConfigurator.Apply(options
                .WithInput(stream)
                .WithOutput(stream), serverOptions, logLevel, fileLogLevel));
        await server.WaitForExit;
        Console.Error.WriteLine("[LSP] Client disconnected - waiting for next connection");
    }
}

{
    var server = await LanguageServer.From(options =>
        ServerConfigurator.Apply(options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput()), serverOptions, logLevel, fileLogLevel));

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