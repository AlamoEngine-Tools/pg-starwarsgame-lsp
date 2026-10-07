// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>
///     The sidecar against the real emmylua_ls, with the engine stubs as library.
/// </summary>
/// <remarks>Local only: set <c>AET_EMMYLUA_LS</c> to the binary.</remarks>
public sealed class RealEmmyluaTest
{
    [Fact]
    public async Task TheRealAnalyzer_ReadsTheStubs_AndReportsOnTheMirroredDocument()
    {
        var binary = Environment.GetEnvironmentVariable("AET_EMMYLUA_LS") ?? "";
        if (!File.Exists(binary))
            Assert.Skip("Set AET_EMMYLUA_LS to an emmylua_ls binary to run against the real analyzer.");

        var work = Path.Combine(Path.GetTempPath(), "aet-emmylua-real-" + Guid.NewGuid().ToString("N"));
        var scripts = Path.Combine(work, "Data", "Scripts");
        Directory.CreateDirectory(scripts);
        var file = Path.Combine(scripts, "probe.lua");
        // A syntax error the analyzer reports, and an engine function only the stubs declare.
        const string text = "local player = Find_Player(\"Empire\")\nlocal broken = {1 2}\n";
        await File.WriteAllTextAsync(file, text);
        var uri = DocumentUri.FromFileSystemPath(file);

        var messages = new ConcurrentQueue<string>();
        await using var host = new LuaAnalyzerHost(new LuaAnalyzerOptions
        {
            Command = binary,
            ConfigDirectory = Path.Combine(work, "config"),
            RequestTimeout = TimeSpan.FromSeconds(10)
        }, NullLogger<LuaAnalyzerHost>.Instance);
        host.DiagnosticsPublished += p =>
        {
            if (p.Uri == uri)
                foreach (var d in p.Diagnostics)
                    messages.Enqueue(d.Code?.String ?? "");
        };

        var stubs = Path.Combine(AppContext.BaseDirectory, "schema", "lua");
        await host.StartAsync(EmmyrcWriter.Plan([new ProjectLayer(0, "Mod", [], [scripts], [], [], null)], stubs));
        host.DidOpen(uri.ToString(), text, 1);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!messages.Contains("syntax-error") && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.Contains("syntax-error", messages);

        var hover = await host.RequestAsync((client, ct) => client.RequestHover(new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 18)
        }, ct));
        Assert.Contains("Find_Player", hover!.Contents.MarkupContent!.Value);

        await host.DisposeAsync();
        try
        {
            Directory.Delete(work, true);
        }
        catch (IOException)
        {
            // released late; temp
        }
    }
}