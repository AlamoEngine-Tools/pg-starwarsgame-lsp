// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>
///     Every edit of an open script is reported on, through the instances as the server runs them: each
///     string of a vanilla script broken in turn, and mended again, is a report each time.
/// </summary>
/// <remarks>Local only, see <see cref="VanillaScriptStack" />.</remarks>
public sealed partial class RealEditSweepTest
{
    private const string Interdictor = "Gameobject/Interdictor.lua";

    [Fact]
    public async Task EveryStringBrokenInTurn_IsReported_AndSoIsItsMending()
    {
        await using var stack = VanillaScriptStack.Create(Interdictor);
        var script = stack.UriOf(Interdictor);
        var reports = new ConcurrentQueue<(TimeSpan At, int Count)>();
        var clock = Stopwatch.StartNew();
        stack.Analyzer.DiagnosticsPublished += p =>
        {
            if (DocumentUris.Same(p.Uri.ToString(), script)) reports.Enqueue((clock.Elapsed, p.Diagnostics.Count()));
        };

        var original = await File.ReadAllTextAsync(stack.FileHelper.FileUriToPath(script)!,
            TestContext.Current.CancellationToken);
        var version = 1;
        stack.Analyzer.DidOpen(script, original, version);
        await VanillaScriptStack.UntilAsync(() => !reports.IsEmpty, TimeSpan.FromSeconds(30));
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
                stack.Analyzer.DidChange(script, text, ++version);
                await VanillaScriptStack.UntilAsync(() => reports.Count > before, TimeSpan.FromSeconds(4));
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
    }

    // The same sweep, the analyzer alone: a copy of the script and its library as its workspace, the
    // same stubs as its library.
    [Fact]
    public async Task EveryStringBrokenInTurn_IsReported_ByTheAnalyzerAlone()
    {
        var (binary, scripts) = VanillaScriptStack.Prerequisites();
        var work = Path.Combine(Path.GetTempPath(), "aet-emmylua-sweep-bare-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(work, "root");
        Directory.CreateDirectory(Path.Combine(root, "Library"));
        Directory.CreateDirectory(Path.Combine(root, "Gameobject"));
        foreach (var path in Directory.GetFiles(Path.Combine(scripts, "Library"), "*.lua"))
            File.Copy(path, Path.Combine(root, "Library", Path.GetFileName(path)));
        var file = Path.Combine(root, "Gameobject", "Interdictor.lua");
        File.Copy(Path.Combine(scripts, Interdictor), file);
        var stubDirectory = Path.Combine(AppContext.BaseDirectory, "schema", "lua");
        var library = Directory.GetFiles(stubDirectory, "*.d.lua")
            .Where(f => !Path.GetFileName(f).StartsWith("pg", StringComparison.OrdinalIgnoreCase)).ToList();

        var uri = DocumentUri.FromFileSystemPath(file);
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
        await VanillaScriptStack.UntilAsync(() => !reports.IsEmpty, TimeSpan.FromSeconds(30));
        Assert.NotEmpty(reports);
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        var silent = new List<string>();
        foreach (var quote in ClosingQuotes(original))
        foreach (var (text, what) in new[] { (original.Remove(quote, 1), "broken"), (original, "mended") })
        {
            var before = reports.Count;
            host.DidChange(uri.ToString(), text, ++version);
            await VanillaScriptStack.UntilAsync(() => reports.Count > before, TimeSpan.FromSeconds(4));
            if (reports.Count == before)
                silent.Add($"{what} at line {original[..quote].Count(c => c == '\n') + 1} (v{version})");
        }

        Assert.True(silent.Count == 0, "edits with no report within 4 s: " + string.Join("; ", silent));
        // ReSharper disable once DisposeOnUsingVariable - stopped before the directory goes; the using covers the failure path
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

    // The offset of each string literal's closing quote outside comments, at most a dozen.
    private static List<int> ClosingQuotes(string text)
    {
        return StringLiteral().Matches(text).Where(m => !InComment(text, m.Index))
            .Select(m => m.Index + m.Length - 1).Take(12).ToList();
    }

    private static bool InComment(string text, int at)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        return text.AsSpan(lineStart, at - lineStart).Contains("--", StringComparison.Ordinal);
    }

    [GeneratedRegex("\"[^\"\\n]*\"")]
    private static partial Regex StringLiteral();
}