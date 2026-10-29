// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis.Generated;
using PG.StarWarsGame.LSP.Lua.Analysis.Instances;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>
///     The real analyzer behind the instances and generated documents, as the server runs it, over the
///     vanilla EaW scripts: each library script and the scripts named, indexed with their requires.
/// </summary>
/// <remarks>
///     Local only: <see cref="Create" /> skips the test unless <c>AET_EMMYLUA_LS</c> names the binary
///     and the vanilla EaW scripts are under <c>eaw/Data/Scripts</c> at the repository root.
/// </remarks>
internal sealed partial class VanillaScriptStack : IAsyncDisposable
{
    private readonly string _work;

    private VanillaScriptStack(string scripts, string work, LuaAnalyzerInstances instances,
        GeneratedAnalyzerDocuments analyzer, FileHelper fileHelper)
    {
        Scripts = scripts;
        _work = work;
        Instances = instances;
        Analyzer = analyzer;
        FileHelper = fileHelper;
    }

    /// <summary>The vanilla scripts' folder.</summary>
    public string Scripts { get; }

    public LuaAnalyzerInstances Instances { get; }

    /// <summary>What the handlers talk to.</summary>
    public GeneratedAnalyzerDocuments Analyzer { get; }

    public FileHelper FileHelper { get; }

    public async ValueTask DisposeAsync()
    {
        await Instances.DisposeAsync();
        try
        {
            Directory.Delete(_work, true);
        }
        catch (IOException)
        {
            // released late; temp
        }
    }

    /// <param name="scripts">
    ///     Paths below <c>Data/Scripts</c> indexed beside the library, such as
    ///     <c>Gameobject/Interdictor.lua</c>.
    /// </param>
    public static VanillaScriptStack Create(params string[] scripts)
    {
        var (binary, root) = Prerequisites();

        var work = Path.Combine(Path.GetTempPath(), "aet-emmylua-vanilla-" + Guid.NewGuid().ToString("N"));
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
        foreach (var path in Directory.GetFiles(Path.Combine(root, "Library"), "*.lua")
                     .Concat(scripts.Select(s => Path.Combine(root, s))))
        {
            var uri = fileHelper.NormalizeUri(fileHelper.PathToFileUri(path));
            var requires = Require().Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value)
                .ToImmutableArray();
            documents[uri] = new DocumentIndex(uri, 1, [], [], [.. requires], LayerRank: 0);
        }

        var index = new FakeGameIndexService(GameIndex.Empty with
        {
            Documents = documents.ToImmutableDictionary(DocumentUris.Comparer)
        });
        // The annotations the server writes into what the analyzer reads, the state handlers' among them.
        var parseCache = new LuaParseCache(new DocumentTextSource(new FakeGameWorkspaceHost(), fileHelper,
            NullLogger<DocumentTextSource>.Instance), 64, NullLogger<LuaParseCache>.Instance);
        var texts = new LuaGeneratedTexts(
            [
                new StubOverrideAnnotations(schema), new StateHandlerAnnotations(index, parseCache, fileHelper),
                new TaskForceAnnotations()
            ],
            fileHelper);
        var instances = new LuaAnalyzerInstances(directory => new LuaAnalyzerHost(new LuaAnalyzerOptions
            {
                Command = binary, ConfigDirectory = directory, RequestTimeout = TimeSpan.FromSeconds(10)
            }, NullLogger<LuaAnalyzerHost>.Instance), index, fileHelper, stubs, texts,
            Path.Combine(work, "instances", "1"), NullLogger<LuaAnalyzerInstances>.Instance);
        instances.Configure([new ProjectLayer(0, "Game", [], [root.ToLowerInvariant()], [], [], null)]);
        return new VanillaScriptStack(root, work, instances, new GeneratedAnalyzerDocuments(instances, texts),
            fileHelper);
    }

    /// <summary>The analyzer binary and the vanilla scripts' folder; the test is skipped without either.</summary>
    public static (string Binary, string Scripts) Prerequisites()
    {
        var binary = Environment.GetEnvironmentVariable("AET_EMMYLUA_LS") ?? "";
        if (!File.Exists(binary))
            Assert.Skip("Set AET_EMMYLUA_LS to an emmylua_ls binary to run against the real analyzer.");
        var scripts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "eaw", "Data",
            "Scripts"));
        if (!Directory.Exists(scripts)) Assert.Skip("The vanilla EaW scripts are not at " + scripts);
        return (binary, scripts);
    }

    /// <summary>The URI of a script below <c>Data/Scripts</c>, as the server keys it.</summary>
    public string UriOf(string script)
    {
        return FileHelper.NormalizeUri(FileHelper.PathToFileUri(Path.Combine(Scripts, script)));
    }

    public static async Task UntilAsync(Func<bool> done, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (!done() && DateTime.UtcNow < deadline) await Task.Delay(50, TestContext.Current.CancellationToken);
    }

    [GeneratedRegex("""require\s*\(?\s*"([^"]+)"\s*\)?""")]
    private static partial Regex Require();
}