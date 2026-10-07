// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The server over plain JSON-RPC, with every message it sends kept as written.
/// </summary>
/// <remarks>
///     The protocol client the other tests use files registrations by id, so a registration sent
///     twice under one id reads as one - and VS Code keeps both, and offers the user two formatters.
///     Counting what crossed the wire is the only check that sees it.
/// </remarks>
public sealed class RawLspSession : IAsyncDisposable
{
    private readonly TaskCompletionSource<JObject> _initializeResult =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Process _process;
    private readonly ConcurrentQueue<JObject> _received = new();
    private readonly TaskCompletionSource _scanComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _nextId;

    private RawLspSession(Process process)
    {
        _process = process;
        _ = Task.Run(ReadLoopAsync);
        _ = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is not null)
            {
            }
        });
    }

    /// <summary>The capabilities object of the initialize result.</summary>
    public JObject Capabilities { get; private set; } = new();

    /// <summary>Every method the server registered dynamically, once per registration sent.</summary>
    public IReadOnlyList<string> RegisteredMethods => _received
        .Where(m => (string?)m["method"] == "client/registerCapability")
        .SelectMany(m => m["params"]?["registrations"] ?? new JArray())
        .Select(r => (string)r["method"]!)
        .ToList();

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(true);
            await _process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            // already gone
        }

        _process.Dispose();
    }

    /// <summary>Starts the server, initializes it, and waits until its workspace scan is complete.</summary>
    public static async Task<RawLspSession> StartAsync(object clientCapabilities, object initializationOptions)
    {
        var assemblyDir = Path.GetDirectoryName(typeof(RawLspSession).Assembly.Location)!;
        var startInfo = new ProcessStartInfo("dotnet",
            $"\"{Path.Combine(assemblyDir, "PG.StarWarsGame.LSP.Server.dll")}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var session = new RawLspSession(Process.Start(startInfo)!);

        var workspace = Path.Combine(assemblyDir, "TestData");
        await session.SendAsync(new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref session._nextId),
            method = "initialize",
            @params = new
            {
                processId = Environment.ProcessId,
                rootUri = new Uri(workspace).AbsoluteUri,
                capabilities = clientCapabilities,
                initializationOptions
            }
        });
        var result = await session._initializeResult.Task.WaitAsync(TimeSpan.FromSeconds(30));
        session.Capabilities = (JObject?)result["capabilities"] ?? new JObject();
        await session.SendAsync(new { jsonrpc = "2.0", method = "initialized", @params = new { } });
        await session._scanComplete.Task.WaitAsync(TimeSpan.FromSeconds(60));
        return session;
    }

    private async Task SendAsync(object message)
    {
        var body = JsonConvert.SerializeObject(message);
        var bytes = Encoding.UTF8.GetBytes($"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}");
        await _writeLock.WaitAsync();
        try
        {
            await _process.StandardInput.BaseStream.WriteAsync(bytes);
            await _process.StandardInput.BaseStream.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        var stream = _process.StandardOutput.BaseStream;
        try
        {
            while (true)
            {
                var length = await ReadHeaderAsync(stream);
                if (length < 0) return;
                var body = new byte[length];
                await stream.ReadExactlyAsync(body);
                var message = JObject.Parse(Encoding.UTF8.GetString(body));
                _received.Enqueue(message);

                var method = (string?)message["method"];
                if (method is null && message["result"] is JObject result) _initializeResult.TrySetResult(result);
                else if (method == "$/workspaceScanComplete") _scanComplete.TrySetResult();
                else if (method is not null && message["id"] is { } id)
                    await SendAsync(new { jsonrpc = "2.0", id, result = (object?)null });
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or EndOfStreamException)
        {
            // server gone
        }
    }

    private static async Task<int> ReadHeaderAsync(Stream stream)
    {
        var header = new StringBuilder();
        var one = new byte[1];
        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(one) == 0) return -1;
            header.Append((char)one[0]);
        }

        var line = header.ToString().Split("\r\n").First(l => l.StartsWith("Content-Length:", StringComparison.Ordinal));
        return int.Parse(line["Content-Length:".Length..].Trim());
    }
}
