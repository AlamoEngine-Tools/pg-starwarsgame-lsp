// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis.Generated;
using PG.StarWarsGame.LSP.Lua.Analysis.Instances;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>
///     Every edit of an open script is reported on, through the instances as the server runs them: each
///     string of a vanilla script broken in turn, and mended again, is a report each time.
/// </summary>
/// <remarks>
///     Local only: set <c>AET_EMMYLUA_LS</c> to the binary, with the vanilla EaW scripts under
///     <c>eaw/Data/Scripts</c> at the repository root.
/// </remarks>
public sealed partial class RealEditSweepTest
{
    [Fact]
    public async Task EveryStringBrokenInTurn_IsReported_AndSoIsItsMending()
    {
        var binary = Environment.GetEnvironmentVariable("AET_EMMYLUA_LS") ?? "";
        if (!File.Exists(binary))
            Assert.Skip("Set AET_EMMYLUA_LS to an emmylua_ls binary to run against the real analyzer.");
        var scripts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "eaw",
            "Data", "Scripts"));
        if (!Directory.Exists(scripts)) Assert.Skip("The vanilla EaW scripts are not at " + scripts);

        var work = Path.Combine(Path.GetTempPath(), "aet-emmylua-sweep-" + Guid.NewGuid().ToString("N"));
        var fileHelper = new FileHelper(new FileSystem());
        var stubDirectory = Path.Combine(AppContext.BaseDirectory, "schema", "lua");
        var stubFiles = Directory.GetFiles(stubDirectory, "*.d.lua").Select(Path.GetFileName).OfType<string>()
            .ToList();
        var stubs = new LuaStubLocation
        {
            Directory = stubDirectory,
            Files = stubFiles,
            Libraries = stubFiles.Where(f => f.StartsWith("pg", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(f => f[..^".d.lua".Length], f => f, StringComparer.OrdinalIgnoreCase)
        };
        var schema = LuaApiSchemaProvider.FromFiles(stubs.Files.Select(f =>
            new LuaStubFile(f, File.ReadAllText(Path.Combine(stubDirectory, f)))));

        var documents = new Dictionary<string, DocumentIndex>(DocumentUris.Comparer);
        foreach (var path in Directory.GetFiles(Path.Combine(scripts, "Library"), "*.lua")
                     .Append(Path.Combine(scripts, "Gameobject", "Interdictor.lua")))
        {
            var uri = fileHelper.NormalizeUri(fileHelper.PathToFileUri(path));
            var requires = Require().Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value).ToImmutableArray();
            documents[uri] = new DocumentIndex(uri, 1, [], [], [.. requires], LayerRank: 0);
        }

        var index = new FakeGameIndexService(GameIndex.Empty with
        {
            Documents = documents.ToImmutableDictionary(DocumentUris.Comparer)
        });
        var texts = new LuaGeneratedTexts([new StubOverrideAnnotations(schema)], fileHelper);
        await using var instances = new LuaAnalyzerInstances(directory => new LuaAnalyzerHost(new LuaAnalyzerOptions
            {
                Command = binary, ConfigDirectory = directory, RequestTimeout = TimeSpan.FromSeconds(10)
            }, NullLogger<LuaAnalyzerHost>.Instance), index, fileHelper, stubs, texts,
            Path.Combine(work, "instances", "1"), NullLogger<LuaAnalyzerInstances>.Instance);
        var analyzer = new GeneratedAnalyzerDocuments(instances, texts);
        instances.Configure([new ProjectLayer(0, "Game", [], [scripts.ToLowerInvariant()], [], [], null)]);

        var script = fileHelper.NormalizeUri(fileHelper.PathToFileUri(Path.Combine(scripts, "Gameobject",
            "Interdictor.lua")));
        var reports = new ConcurrentQueue<(TimeSpan At, int Count)>();
        var clock = Stopwatch.StartNew();
        analyzer.DiagnosticsPublished += p =>
        {
            if (DocumentUris.Same(p.Uri.ToString(), script)) reports.Enqueue((clock.Elapsed, p.Diagnostics.Count()));
        };

        var original = await File.ReadAllTextAsync(fileHelper.FileUriToPath(script)!,
            TestContext.Current.CancellationToken);
        var version = 1;
        analyzer.DidOpen(script, original, version);
        await UntilAsync(() => !reports.IsEmpty, TimeSpan.FromSeconds(30));
        Assert.NotEmpty(reports);
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        var silent = new List<string>();
        var latencies = new List<double>();
        foreach (var quote in ClosingQuotes(original))
        {
            var broken = original.Remove(quote, 1);
            foreach (var (text, what) in new[] { (broken, "broken"), (original, "mended") })
            {
                var before = reports.Count;
                var sent = clock.Elapsed;
                analyzer.DidChange(script, text, ++version);
                await UntilAsync(() => reports.Count > before, TimeSpan.FromSeconds(4));
                if (reports.Count == before)
                    silent.Add($"{what} at line {original[..quote].Count(c => c == '\n') + 1} (v{version})");
                else
                    latencies.Add((reports.Skip(before).First().At - sent).TotalMilliseconds);
            }
        }

        latencies.Sort();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"edit -> report, ms: min {latencies.FirstOrDefault():F0}, median " +
            $"{(latencies.Count == 0 ? 0 : latencies[latencies.Count / 2]):F0}, max {latencies.LastOrDefault():F0}");
        Assert.True(silent.Count == 0, "edits with no report within 4 s: " + string.Join("; ", silent));

        // ReSharper disable once DisposeOnUsingVariable - stopped before the directory goes; the using covers the failure path
        await instances.DisposeAsync();
        try
        {
            Directory.Delete(work, true);
        }
        catch (IOException)
        {
            // released late; temp
        }
    }

    // The same sweep, the analyzer alone: a copy of the script and its library as its workspace, the
    // same stubs as its library.
    [Fact]
    public async Task EveryStringBrokenInTurn_IsReported_ByTheAnalyzerAlone()
    {
        var binary = Environment.GetEnvironmentVariable("AET_EMMYLUA_LS") ?? "";
        if (!File.Exists(binary))
            Assert.Skip("Set AET_EMMYLUA_LS to an emmylua_ls binary to run against the real analyzer.");
        var scripts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "eaw",
            "Data", "Scripts"));
        if (!Directory.Exists(scripts)) Assert.Skip("The vanilla EaW scripts are not at " + scripts);

        var work = Path.Combine(Path.GetTempPath(), "aet-emmylua-sweep-bare-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(work, "root");
        Directory.CreateDirectory(Path.Combine(root, "Library"));
        Directory.CreateDirectory(Path.Combine(root, "Gameobject"));
        foreach (var path in Directory.GetFiles(Path.Combine(scripts, "Library"), "*.lua"))
            File.Copy(path, Path.Combine(root, "Library", Path.GetFileName(path)));
        var file = Path.Combine(root, "Gameobject", "Interdictor.lua");
        File.Copy(Path.Combine(scripts, "Gameobject", "Interdictor.lua"), file);
        var stubDirectory = Path.Combine(AppContext.BaseDirectory, "schema", "lua");
        var library = Directory.GetFiles(stubDirectory, "*.d.lua")
            .Where(f => !Path.GetFileName(f).StartsWith("pg", StringComparison.OrdinalIgnoreCase)).ToList();

        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.FromFileSystemPath(file);
        var reports = new ConcurrentQueue<int>();
        await using var host = new LuaAnalyzerHost(new LuaAnalyzerOptions
        {
            Command = binary, ConfigDirectory = Path.Combine(work, "config"), RequestTimeout = TimeSpan.FromSeconds(10)
        }, NullLogger<LuaAnalyzerHost>.Instance);
        host.DiagnosticsPublished += p =>
        {
            if (p.Uri == uri) reports.Enqueue(p.Diagnostics.Count());
        };
        await host.StartAsync(new AnalyzerPlan([root], library));

        var original = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        var version = 1;
        host.DidOpen(uri.ToString(), original, version);
        await UntilAsync(() => !reports.IsEmpty, TimeSpan.FromSeconds(30));
        Assert.NotEmpty(reports);
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        var silent = new List<string>();
        foreach (var quote in ClosingQuotes(original))
        foreach (var (text, what) in new[] { (original.Remove(quote, 1), "broken"), (original, "mended") })
        {
            var before = reports.Count;
            host.DidChange(uri.ToString(), text, ++version);
            await UntilAsync(() => reports.Count > before, TimeSpan.FromSeconds(4));
            if (reports.Count == before)
                silent.Add($"{what} at line {original[..quote].Count(c => c == '\n') + 1} (v{version})");
        }

        Assert.True(silent.Count == 0, "edits with no report within 4 s: " + string.Join("; ", silent));
    }

    // The offset of each string literal's closing quote outside comments, at most a dozen.
    private static IEnumerable<int> ClosingQuotes(string text)
    {
        return StringLiteral().Matches(text).Where(m => !InComment(text, m.Index))
            .Select(m => m.Index + m.Length - 1).Take(12).ToList();
    }

    private static bool InComment(string text, int at)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        return text.AsSpan(lineStart, at - lineStart).Contains("--", StringComparison.Ordinal);
    }

    private static async Task UntilAsync(Func<bool> done, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (!done() && DateTime.UtcNow < deadline) await Task.Delay(50, TestContext.Current.CancellationToken);
    }

    [GeneratedRegex("""require\s*\(?\s*"([^"]+)"\s*\)?""")]
    private static partial Regex Require();

    [GeneratedRegex("\"[^\"\\n]*\"")]
    private static partial Regex StringLiteral();
}
