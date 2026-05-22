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

public sealed class LspServerFixture : IAsyncLifetime
{
    private readonly TaskCompletionSource _scanStartedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Process? _process;
    private TcpClient? _tcpClient;
    private LanguageClient? _client;

    public LanguageClient Client => _client!;
    public string TestDataDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Completes when the server has published its first diagnostics notification,
    /// which signals that the workspace scan has started and the FileTypeRegistry is populated.
    /// </summary>
    public Task ScanStarted => _scanStartedTcs.Task;

    public async Task InitializeAsync()
    {
        TestDataDirectory = Path.Combine(
            Path.GetDirectoryName(typeof(LspServerFixture).Assembly.Location)!,
            "TestData");

        var workspacePath = LspTestEnvironment.WorkspacePath ?? TestDataDirectory;

        if (LspTestEnvironment.ExternalServerPort is { } port)
        {
            await InitializeExternalAsync(workspacePath, port);
        }
        else
        {
            await InitializeSpawnedAsync(workspacePath);
        }
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
        // Register before the client starts dispatching so we never miss early notifications.
        options.OnPublishDiagnostics(_ => _scanStartedTcs.TrySetResult());
    }

    public async Task DisposeAsync()
    {
        if (_client is IAsyncDisposable asyncClient)
            await asyncClient.DisposeAsync();
        else
            (_client as IDisposable)?.Dispose();

        _tcpClient?.Dispose();

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
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
    }

    private static object BuildInitOptions()
    {
        return new
        {
            schemaLocalPath = LspTestEnvironment.SchemaLocalPath,
            gamePath = LspTestEnvironment.GamePath,
            baselineLocalPath = LspTestEnvironment.BaselineLocalPath,
            baselineType = LspTestEnvironment.BaselineLocalPath is null ? "None" : (string?)null,
            locale = LspTestEnvironment.Locale,
            modPaths = LspTestEnvironment.ModPaths.Count > 0
                ? LspTestEnvironment.ModPaths.ToArray()
                : (string[]?)null
        };
    }
}
