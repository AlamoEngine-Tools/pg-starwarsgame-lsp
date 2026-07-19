// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;

namespace PG.StarWarsGame.LSP.Lua.Tests;

/// <summary>
///     Runs the full game Lua corpus through Loretta to determine which syntax preset
///     parses cleanly and what dialect quirks exist.
/// </summary>
[Trait("Category", "E2E")]
public sealed class LorrettaParserSmokeTest
{
    private readonly ITestOutputHelper _output;

    public LorrettaParserSmokeTest(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Primary target preset: closest standard match to the game's Lua 5.0/5.1 dialect.</summary>
    [Fact]
    public void CorpusParsesWithoutErrors_Lua51()
    {
        var scriptsDir = RequireScriptsDirectory();
        ParseCorpusAndAssert(scriptsDir, LuaSyntaxOptions.Lua51, "Lua51");
    }

    /// <summary>
    ///     Maximum-tolerance baseline: enables every Loretta extension flag.
    ///     If Lua51 has errors but All does not, the delta shows exactly which flags we need.
    /// </summary>
    [Fact]
    public void CorpusParsesWithoutErrors_AllOptions()
    {
        var scriptsDir = RequireScriptsDirectory();
        ParseCorpusAndAssert(scriptsDir, LuaSyntaxOptions.All, "All");
    }

    /// <summary>
    ///     Explores which individual <see cref="LuaSyntaxOptions" /> flags eliminate the
    ///     remaining Lua51 errors so we can define a minimal custom preset for L-1.
    /// </summary>
    [Fact]
    public void ExploreOptionsFlags()
    {
        var scriptsDir = RequireScriptsDirectory();
        var files = GetLuaFiles(scriptsDir);

        var presets = new (string Name, LuaSyntaxOptions Options)[]
        {
            ("Lua51", LuaSyntaxOptions.Lua51),
            ("Lua52", LuaSyntaxOptions.Lua52),
            ("Lua53", LuaSyntaxOptions.Lua53),
            ("Lua54", LuaSyntaxOptions.Lua54),
            ("LuaJIT20", LuaSyntaxOptions.LuaJIT20),
            ("LuaJIT21", LuaSyntaxOptions.LuaJIT21),
            ("GMod", LuaSyntaxOptions.GMod),
            ("All", LuaSyntaxOptions.All)
        };

        _output.WriteLine($"Corpus: {files.Length} files\n");
        _output.WriteLine($"{"Preset",-12} {"Files with errors",18} {"Total errors",14}");
        _output.WriteLine(new string('-', 46));

        foreach (var (name, options) in presets)
        {
            var (fileErrors, totalErrors) = CountErrors(scriptsDir, files, options);
            _output.WriteLine($"{name,-12} {fileErrors,18} {totalErrors,14}");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────

    private void ParseCorpusAndAssert(string scriptsDir, LuaSyntaxOptions options, string presetName)
    {
        var files = GetLuaFiles(scriptsDir);
        _output.WriteLine($"[{presetName}] Parsing {files.Length} files...\n");

        var errors = CollectErrors(scriptsDir, files, options);

        if (errors.Count > 0)
        {
            var grouped = errors
                .GroupBy(e => e.Message)
                .OrderByDescending(g => g.Count())
                .ToList();

            _output.WriteLine($"=== {errors.Count} errors across {grouped.Count} distinct messages ===\n");
            foreach (var group in grouped)
            {
                _output.WriteLine($"  [{group.Count(),3}×] {group.Key}");
                foreach (var (path, _, line, col) in group.Take(5))
                    _output.WriteLine($"        {path}:{line}:{col}");
            }
        }

        _output.WriteLine($"\nResult [{presetName}]: {files.Length} files, {errors.Count} errors.");
        Assert.Empty(errors);
    }

    private static (int FileErrors, int TotalErrors) CountErrors(
        string scriptsDir, string[] files, LuaSyntaxOptions options)
    {
        var parseOptions = new LuaParseOptions(options);
        var totalErrors = 0;
        var filesWithErrors = 0;
        foreach (var file in files)
        {
            var text = File.ReadAllText(file, Encoding.UTF8);
            var tree = LuaSyntaxTree.ParseText(text, parseOptions, file);
            var fileErrorCount = tree.GetDiagnostics()
                .Count(d => d.Severity == DiagnosticSeverity.Error);
            if (fileErrorCount > 0)
            {
                filesWithErrors++;
                totalErrors += fileErrorCount;
            }
        }

        return (filesWithErrors, totalErrors);
    }

    private static List<(string RelativePath, string Message, int Line, int Col)> CollectErrors(
        string scriptsDir, string[] files, LuaSyntaxOptions options)
    {
        var parseOptions = new LuaParseOptions(options);
        var errors = new List<(string, string, int, int)>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file, Encoding.UTF8);
            var tree = LuaSyntaxTree.ParseText(text, parseOptions, file);
            foreach (var diag in tree.GetDiagnostics())
            {
                if (diag.Severity != DiagnosticSeverity.Error)
                    continue;
                var span = diag.Location.GetLineSpan();
                errors.Add((
                    Path.GetRelativePath(scriptsDir, file),
                    diag.GetMessage(),
                    span.StartLinePosition.Line + 1,
                    span.StartLinePosition.Character + 1));
            }
        }

        return errors;
    }

    private static string[] GetLuaFiles(string scriptsDir)
    {
        return Directory.GetFiles(scriptsDir, "*.lua", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToArray();
    }

    private static string RequireScriptsDirectory()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(LorrettaParserSmokeTest).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var scripts = Path.Combine(dir.FullName, "foc", "Data", "Scripts");
                if (Directory.Exists(scripts))
                    return scripts;
                break;
            }

            dir = dir.Parent;
        }

        throw new Exception("$XunitDynamicSkip$Could not find foc/Data/Scripts - is the game data in the repo?");
    }
}