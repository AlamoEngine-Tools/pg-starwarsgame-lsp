// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

public class LspServerFixture : IAsyncLifetime
{
    private readonly TaskCompletionSource _scanCompleteTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _scanStartedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private LanguageClient? _client;

    private Process? _process;
    private TcpClient? _tcpClient;

    public LanguageClient Client => _client!;
    public string TestDataDirectory { get; private set; } = string.Empty;

    /// <summary>
    ///     Completes when the server has published its first diagnostics notification,
    ///     which signals that the workspace scan has started and the FileTypeRegistry is populated.
    /// </summary>
    public Task ScanStarted => _scanStartedTcs.Task;

    /// <summary>
    ///     Completes only when the server sends the <c>$/workspaceScanComplete</c> notification,
    ///     meaning the full workspace index is populated. Use this instead of
    ///     <see cref="ScanStarted" /> when tests depend on the complete index.
    /// </summary>
    public Task ScanCompleted => _scanCompleteTcs.Task;

    public async ValueTask InitializeAsync()
    {
        TestDataDirectory = Path.Combine(
            Path.GetDirectoryName(typeof(LspServerFixture).Assembly.Location)!,
            "TestData");

        var workspacePath = ResolveWorkspacePath();

        if (LspTestEnvironment.ExternalServerPort is { } port)
            await InitializeExternalAsync(workspacePath, port);
        else
            await InitializeSpawnedAsync(workspacePath);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is IAsyncDisposable asyncClient)
            await asyncClient.DisposeAsync();
        else
            (_client as IDisposable)?.Dispose();

        _tcpClient?.Dispose();

        if (_process is not null)
            try
            {
                if (!_process.HasExited)
                    _process.Kill(true);
            }
            catch
            {
                // ignore cleanup errors
            }
            finally
            {
                _process.Dispose();
            }
    }

    protected virtual string ResolveWorkspacePath()
    {
        return LspTestEnvironment.WorkspacePath ?? TestDataDirectory;
    }

    /// <summary>
    ///     Fired on every <c>publishDiagnostics</c> notification received from the server.
    ///     Use this instead of <c>client.Register(r => r.OnPublishDiagnostics(...))</c>,
    ///     which does not reliably receive server-push notifications in OmniSharp 0.19.x.
    /// </summary>
    public event Action<PublishDiagnosticsParams>? DiagnosticsReceived;

    /// <summary>
    ///     Returns a task that completes with the first <c>publishDiagnostics</c> notification
    ///     for <paramref name="uri" />, or faults with <see cref="TaskCanceledException" />
    ///     after <paramref name="timeout" />.
    /// </summary>
    public Task<PublishDiagnosticsParams> WaitForDiagnosticsAsync(DocumentUri uri, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<PublishDiagnosticsParams>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var uriStr = uri.ToString();

        void Handler(PublishDiagnosticsParams p)
        {
            if (!string.Equals(p.Uri.ToString(), uriStr, StringComparison.OrdinalIgnoreCase)) return;
            DiagnosticsReceived -= Handler;
            tcs.TrySetResult(p);
        }

        DiagnosticsReceived += Handler;
        _ = Task.Delay(timeout).ContinueWith(_ =>
        {
            DiagnosticsReceived -= Handler;
            tcs.TrySetCanceled();
        }, TaskScheduler.Default);

        return tcs.Task;
    }

    private async Task InitializeExternalAsync(string workspacePath, int port)
    {
        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, port);
        _tcpClient = tcpClient;

        var stream = tcpClient.GetStream();
        _client = await LanguageClient.From(options =>
        {
            options.Input = PipeReader.Create(stream);
            options.Output = PipeWriter.Create(stream);
            ConfigureClientOptions(options, workspacePath);
        });
    }

    private async Task InitializeSpawnedAsync(string workspacePath)
    {
        var assemblyDir = Path.GetDirectoryName(typeof(LspServerFixture).Assembly.Location)!;
        var serverDll = Path.Combine(assemblyDir, "PG.StarWarsGame.LSP.Server.dll");

        var startInfo = new ProcessStartInfo("dotnet", $"\"{serverDll}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        _process = Process.Start(startInfo)!;

        _ = Task.Run(async () =>
        {
            try
            {
                while (await _process.StandardError.ReadLineAsync() is { } line)
                    Console.Error.WriteLine($"[LSP] {line}");
            }
            catch
            {
                // process exited
            }
        });

        _client = await LanguageClient.From(options =>
        {
            options.Input = PipeReader.Create(_process.StandardOutput.BaseStream);
            options.Output = PipeWriter.Create(_process.StandardInput.BaseStream);
            ConfigureClientOptions(options, workspacePath);
        });
    }

    private void ConfigureClientOptions(LanguageClientOptions options, string workspacePath)
    {
        options.RootUri = DocumentUri.FromFileSystemPath(workspacePath);
        options.InitializationOptions = BuildInitOptions();
        options.ClientCapabilities = new ClientCapabilities
        {
            TextDocument = new TextDocumentClientCapabilities
            {
                Completion = new CompletionCapability
                {
                    CompletionItem = new CompletionItemCapabilityOptions
                    {
                        SnippetSupport = false
                    }
                },
                Hover = new HoverCapability
                {
                    ContentFormat = new Container<MarkupKind>(MarkupKind.Markdown)
                },
                PublishDiagnostics = new PublishDiagnosticsCapability()
            }
        };
        // Registered here (before From() returns) so the notification is never missed.
        // The typed overload fails to deserialize absent params; use the parameterless one.
        options.OnNotification("$/workspaceScanComplete", () =>
        {
            _scanStartedTcs.TrySetResult();
            _scanCompleteTcs.TrySetResult();
        });
        // Route all incoming publishDiagnostics through the fixture so tests can await
        // them without fighting OmniSharp's dynamic-registration limitations.
        options.OnPublishDiagnostics(p =>
        {
            _scanStartedTcs.TrySetResult();
            DiagnosticsReceived?.Invoke(p);
        });
    }

    private static object BuildInitOptions()
    {
        return new
        {
            schemaLocalPath = LspTestEnvironment.SchemaLocalPath,
            gamePath = LspTestEnvironment.GamePath,
            baselineLocalPath = LspTestEnvironment.BaselineLocalPath,
            baselineType = LspTestEnvironment.BaselineLocalPath is null ? "None" : null,
            locale = LspTestEnvironment.Locale
        };
    }
}