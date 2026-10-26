// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis.Generated;
using PG.StarWarsGame.LSP.Lua.Analysis.Instances;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>
///     The real analyzer, given script instances: a script sees what it requires - a library a lower
///     layer ships, typed by its stub - and never another script's globals.
/// </summary>
/// <remarks>Local only: set <c>AET_EMMYLUA_LS</c> to the binary.</remarks>
public sealed class RealInstancesTest
{
    [Fact]
    public async Task AScript_SeesItsLibraryTypedByTheStub_AndNotAnotherScriptsGlobal()
    {
        var binary = Environment.GetEnvironmentVariable("AET_EMMYLUA_LS") ?? "";
        if (!File.Exists(binary))
            Assert.Skip("Set AET_EMMYLUA_LS to an emmylua_ls binary to run against the real analyzer.");

        var work = Path.Combine(Path.GetTempPath(), "aet-emmylua-instances-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(work, "Game", "Data", "Scripts");
        var mod = Path.Combine(work, "Mod", "Data", "Scripts");
        Directory.CreateDirectory(Path.Combine(game, "Library"));
        Directory.CreateDirectory(mod);
        var fileHelper = new FileHelper(new FileSystem());
        var library = fileHelper.PathToFileUri(Path.Combine(game, "Library", "PGBase.lua"));
        var script = fileHelper.PathToFileUri(Path.Combine(mod, "A.lua"));
        var other = fileHelper.PathToFileUri(Path.Combine(mod, "B.lua"));
        await File.WriteAllTextAsync(fileHelper.FileUriToPath(library)!,
            "function TestValid(wrapper)\n\tif wrapper == nil then\n\t\treturn false\n\tend\n" +
            "\treturn wrapper.Is_Valid and wrapper.Is_Valid()\nend\n", TestContext.Current.CancellationToken);
        const string scriptText = "require(\"PGBase\")\nlocal ok = TestValid(nil)\nlocal t = transport\n";

        var stubDirectory = Path.Combine(AppContext.BaseDirectory, "schema", "lua");
        var stubs = new LuaStubLocation
        {
            Directory = stubDirectory,
            Files = ["api.d.lua", "stdlib.d.lua", "globals.d.lua", "pgbase.d.lua"],
            Libraries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["PGBase"] = "pgbase.d.lua" }
        };
        var schema = LuaApiSchemaProvider.FromFiles(stubs.Files.Select(f =>
            new LuaStubFile(f, File.ReadAllText(Path.Combine(stubDirectory, f)))));
        var index = new FixedIndex(new Dictionary<string, DocumentIndex>(DocumentUris.Comparer)
        {
            [script] = new(script, 1, [], [], ["PGBase"], LayerRank: 1),
            [other] = new(other, 1, [], [], [], LayerRank: 1),
            [library] = new(library, 1, [], [], [], LayerRank: 0)
        });
        var texts = new LuaGeneratedTexts([new StubOverrideAnnotations(schema)], fileHelper);
        await using var instances = new LuaAnalyzerInstances(directory => new LuaAnalyzerHost(new LuaAnalyzerOptions
            {
                Command = binary, ConfigDirectory = directory, RequestTimeout = TimeSpan.FromSeconds(10)
            }, NullLogger<LuaAnalyzerHost>.Instance), index, fileHelper, stubs, texts,
            Path.Combine(work, "instances", "1"), NullLogger<LuaAnalyzerInstances>.Instance);
        var analyzer = new GeneratedAnalyzerDocuments(instances, texts);
        instances.Configure([
            new ProjectLayer(0, "Game", [], [game.ToLowerInvariant()], [], [], null),
            new ProjectLayer(1, "Mod", [], [mod.ToLowerInvariant()], [], [], null)
        ]);

        analyzer.DidOpen(other, "transport = 5\n", 1);
        analyzer.DidOpen(script, scriptText, 1);

        var ok = await HoverAsync(analyzer, script, 1, 7, h => !h.Contains("unknown", StringComparison.Ordinal));
        var transport = await HoverAsync(analyzer, script, 2, 11, _ => true);

        Assert.Contains("boolean", ok);
        Assert.DoesNotContain("any", ok);
        Assert.DoesNotContain("integer", transport);

        // ReSharper disable once DisposeOnUsingVariable - disposed early on purpose, to test the behaviour after disposal; the using covers the failure path
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

    // The analyzer answers once it has read the instance; until then a hover comes back empty, or
    // with the value still unknown.
    private static async Task<string> HoverAsync(GeneratedAnalyzerDocuments analyzer, string uri, int line,
        int character, Func<string, bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        var text = "";
        while (DateTime.UtcNow < deadline)
        {
            var hover = await analyzer.RequestAsync<Hover>("textDocument/hover", new HoverParams
            {
                TextDocument = new TextDocumentIdentifier(DocumentUri.From(uri)),
                Position = new Position(line, character)
            }, CancellationToken.None);
            text = hover?.Contents.MarkupContent?.Value ?? "";
            if (text.Length > 0 && ready(text)) return text;
            await Task.Delay(100);
        }

        return text;
    }

    private sealed class FixedIndex(Dictionary<string, DocumentIndex> documents) : IGameIndexService
    {
        public GameIndex Current { get; } = GameIndex.Empty with
        {
            Documents = documents.ToImmutableDictionary(DocumentUris.Comparer)
        };

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public event Action<ILocalisationIndex>? LocalisationChanged
        {
            add { }
            remove { }
        }

        public event Action<GameIndex>? DynamicEnumChanged
        {
            add { }
            remove { }
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }

        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return new Nothing();
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}