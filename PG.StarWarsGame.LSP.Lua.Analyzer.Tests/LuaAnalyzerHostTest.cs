// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>The sidecar against a stand-in analyzer over stdio.</summary>
public sealed class LuaAnalyzerHostTest : IAsyncDisposable
{
    private const string Uri = "file:///d:/mods/mymod/data/scripts/story/test.lua";

    private static readonly string FakeDll =
        Path.Combine(AppContext.BaseDirectory, "PG.StarWarsGame.LSP.Lua.Analyzer.Fake.dll");

    private readonly string _work =
        Path.Combine(Path.GetTempPath(), "aet-analyzer-test-" + Guid.NewGuid().ToString("N"));

    private readonly ConcurrentQueue<string> _messages = new();
    private LuaAnalyzerHost? _host;

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
        try
        {
            Directory.Delete(_work, true);
        }
        catch (IOException)
        {
            // a process still releasing the directory; the temp folder is the OS's to clean
        }
    }

    private LuaAnalyzerHost Host(Dictionary<string, string>? environment = null, string? command = null)
    {
        Directory.CreateDirectory(_work);
        _host = new LuaAnalyzerHost(new LuaAnalyzerOptions
        {
            Command = command ?? "dotnet",
            LeadingArguments = command is null ? [FakeDll] : [],
            ConfigDirectory = _work,
            RequestTimeout = TimeSpan.FromSeconds(2),
            RestartDelay = TimeSpan.FromMilliseconds(100),
            Environment = environment ?? []
        }, NullLogger<LuaAnalyzerHost>.Instance);
        _host.DiagnosticsPublished += p =>
        {
            foreach (var d in p.Diagnostics) _messages.Enqueue($"{p.Uri}|{d.Message}");
        };
        return _host;
    }

    private static AnalyzerPlan Plan()
    {
        return EmmyrcWriter.Plan([
            new ProjectLayer(0, "EaW", [], ["C:/Games/EaW/Data/Scripts"], [], [], null),
            new ProjectLayer(1, "Mod", [], ["D:/Mods/MyMod/Data/Scripts"], [], [], null)
        ], null);
    }

    private async Task<string> MessageAsync(Func<string, bool> match, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var hit = _messages.FirstOrDefault(match);
            if (hit is not null) return hit;
            await Task.Delay(25);
        }

        Assert.Fail($"No message matched; got: {string.Join(" / ", _messages)}");
        return "";
    }

    private static HoverParams HoverAt()
    {
        return new HoverParams
            { TextDocument = new TextDocumentIdentifier(DocumentUri.From(Uri)), Position = new Position(0, 0) };
    }

    [Fact]
    public async Task OpenAndChange_AreMirroredWithFullText()
    {
        var host = Host();
        await host.StartAsync(Plan());

        host.DidOpen(Uri, "x = 1", 1);
        await MessageAsync(m => m.EndsWith("|open v1 len 5", StringComparison.Ordinal));

        host.DidChange(Uri, "x = 12345", 2);
        await MessageAsync(m => m.EndsWith("|change v2 len 9", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheAnalyzer_IsStartedWithTheMeasuredArguments_ItsConfiguration_AndTheLeafAsWorkspace()
    {
        var host = Host();
        await host.StartAsync(Plan());
        host.DidOpen(Uri, "x = 1", 1);

        // The analyzer asks for its configuration once started; the answer is a round trip away.
        var text = "";
        for (var attempt = 0;
             attempt < 50 && !text.Contains("configuration=answered", StringComparison.Ordinal);
             attempt++)
        {
            var hover = await host.RequestAsync((client, ct) => client.RequestHover(HoverAt(), ct));
            text = hover!.Contents.MarkupContent!.Value;
            await Task.Delay(100);
        }

        Assert.Contains("args=--load-stdlib false --editor vscode", text);
        Assert.Contains("configExists=True", text);
        Assert.Contains("folders=file:///D:/Mods/MyMod/Data/Scripts", text, StringComparison.OrdinalIgnoreCase);
        Assert.True(text.Contains("configuration=answered 1", StringComparison.Ordinal), text);
    }

    [Fact]
    public async Task ARequestTheAnalyzerNeverAnswers_ReturnsNothingAfterTheTimeout()
    {
        var host = Host(new Dictionary<string, string> { ["FAKE_HANG_HOVER"] = "1" });
        await host.StartAsync(Plan());

        var started = DateTime.UtcNow;
        var hover = await host.RequestAsync((client, ct) => client.RequestHover(HoverAt(), ct));

        Assert.Null(hover);
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AnAnalyzerThatDies_IsRestarted_AndGetsTheOpenDocumentsAgain()
    {
        var marker = Path.Combine(_work, "crashed");
        var host = Host(new Dictionary<string, string> { ["FAKE_CRASH_ONCE_FILE"] = marker });
        await host.StartAsync(Plan());

        host.DidOpen(Uri, "x = 1", 1);
        await MessageAsync(m => m.EndsWith("|open v1 len 5", StringComparison.Ordinal));
        while (!File.Exists(marker)) await Task.Delay(25);
        _messages.Clear();

        // The replacement process is told about the document without the editor sending it again.
        await MessageAsync(m => m.EndsWith("|open v1 len 5", StringComparison.Ordinal));
        Assert.Equal(2, host.Starts);
    }

    [Fact]
    public async Task Disposing_StopsTheProcess()
    {
        var host = Host();
        await host.StartAsync(Plan());
        Assert.True(host.IsRunning);

        await host.DisposeAsync();

        Assert.False(host.IsRunning);
        Assert.Null(await host.RequestAsync((client, ct) => client.RequestHover(HoverAt(), ct)));
    }

    [Fact]
    public async Task AMissingExecutable_LeavesTheAnalyzerOff_AndEveryCallANoOp()
    {
        var host = Host(command: Path.Combine(_work, "no-such-analyzer.exe"));

        await host.StartAsync(Plan());
        host.DidOpen(Uri, "x = 1", 1);

        Assert.False(host.IsRunning);
        Assert.Null(await host.RequestAsync((client, ct) => client.RequestHover(HoverAt(), ct)));
    }

    [Fact]
    public async Task NoScriptsInTheLeaf_TheAnalyzerIsNotStarted()
    {
        var host = Host();

        await host.StartAsync(EmmyrcWriter.Plan([new ProjectLayer(0, "Mod", [], [], [], [], null)], null));

        Assert.False(host.IsRunning);
        Assert.Equal(0, host.Starts);
    }
}