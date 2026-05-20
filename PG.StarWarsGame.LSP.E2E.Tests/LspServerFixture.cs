// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.IO.Pipelines;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

public sealed class LspServerFixture : IAsyncLifetime
{
    private Process? _process;
    private LanguageClient? _client;

    public LanguageClient Client => _client!;
    public string TestDataDirectory { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        TestDataDirectory = Path.Combine(
            Path.GetDirectoryName(typeof(LspServerFixture).Assembly.Location)!,
            "TestData");

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

        var workspacePath = LspTestEnvironment.WorkspacePath ?? TestDataDirectory;

        _client = await LanguageClient.From(options =>
        {
            options.Input = PipeReader.Create(_process.StandardOutput.BaseStream);
            options.Output = PipeWriter.Create(_process.StandardInput.BaseStream);
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
        });
    }

    public async Task DisposeAsync()
    {
        if (_client is IAsyncDisposable asyncClient)
            await asyncClient.DisposeAsync();
        else
            (_client as IDisposable)?.Dispose();

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
