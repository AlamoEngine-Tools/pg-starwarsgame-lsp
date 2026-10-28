// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using SyntaxTree = Loretta.CodeAnalysis.SyntaxTree;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis.Generated;
using PG.StarWarsGame.LSP.Lua.Analysis.Instances;
using PG.StarWarsGame.LSP.Testing;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis.Instances;

/// <summary>
///     The game runs every script instance in a Lua state of its own, holding the script and what it
///     requires. The analyzer gets exactly that: one analyzer per open script, sent the script, its
///     require tree with the annotations this server knows, and the engine stubs - never another
///     script's globals.
/// </summary>
public sealed class LuaAnalyzerInstancesTest
{
    private const string Script = "file:///C:/Mod/Data/Scripts/GameObject/Interdictor.lua";
    private const string Other = "file:///C:/Mod/Data/Scripts/GameObject/DarthVader.lua";
    private const string Machine = "file:///C:/Game/Data/Scripts/Library/PGStateMachine.lua";
    private const string Base = "file:///C:/Game/Data/Scripts/Library/PGBase.lua";
    private const string Debug = "file:///C:/Game/Data/Scripts/Library/PGDebug.lua";
    private const string Directory = "C:/cache/instances";

    private static readonly IReadOnlyList<ProjectLayer> Layers =
    [
        new(0, "Vanilla", [], ["c:/game/data/scripts"], [], [], null),
        new(1, "Mod", [], ["c:/mod/data/scripts"], [], [], null)
    ];

    private readonly FakeGameIndexService _index = new();
    private readonly MockFileSystem _disk = new();
    private readonly List<FakeProcess> _processes = [];

    private readonly LuaStubLocation _stubs = new()
    {
        Directory = "C:/schema/lua",
        Files = ["api.d.lua", "globals.d.lua", "pgbase.d.lua", "pgdebug.d.lua", "pgstatemachine.d.lua"],
        Libraries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PGBase"] = "pgbase.d.lua", ["PGDebug"] = "pgdebug.d.lua", ["PGStateMachine"] = "pgstatemachine.d.lua"
        }
    };

    public LuaAnalyzerInstancesTest()
    {
        Disk(Machine, "function Definitions() end\n");
        Disk(Base, "function TestValid(wrapper) end\n");
        Disk(Debug, "function DebugMessage(s) end\n");
        Disk(Other, "transport = 5\n");
        Index((Script, ["PGStateMachine"], 1), (Machine, ["PGBase"], 0), (Base, [], 0), (Other, [], 1));
    }

    private void Disk(string uri, string text)
    {
        _disk.AddFile(new FileHelper(_disk).FileUriToPath(uri)!, new MockFileData(text));
    }

    private void Index(params (string Uri, string[] Requires, int Rank)[] scripts)
    {
        _index.Current = GameIndex.Empty with
        {
            Documents = scripts.ToImmutableDictionary(s => s.Uri,
                s => new DocumentIndex(s.Uri, 1, [], [], [.. s.Requires], LayerRank: s.Rank),
                DocumentUris.Comparer)
        };
    }

    private LuaAnalyzerInstances Instances(params ILuaAnnotationSource[] sources)
    {
        return Instances(LuaAnalyzerInstanceTiming.Immediate, TimeProvider.System, sources);
    }

    private LuaAnalyzerInstances Instances(LuaAnalyzerInstanceTiming timing, TimeProvider time,
        params ILuaAnnotationSource[] sources)
    {
        var fileHelper = new FileHelper(_disk);
        return new LuaAnalyzerInstances(_ =>
            {
                var process = new FakeProcess();
                _processes.Add(process);
                return process;
            }, _index, fileHelper, _stubs, new LuaGeneratedTexts(sources, fileHelper), Directory,
            NullLogger<LuaAnalyzerInstances>.Instance, timing: timing, time: time);
    }

    private static string PlaceIn(FakeProcess process, string real)
    {
        return process.Texts.Keys.Single(k =>
            k.EndsWith(real[real.IndexOf("/Data/Scripts/", StringComparison.Ordinal)..]
                ["/Data/Scripts".Length..], StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnOpenScript_GetsAnAnalyzerOfItsOwn_HoldingTheScriptAndItsRequireTree_AndNothingElse()
    {
        var instances = Instances();
        instances.Configure(Layers);

        instances.DidOpen(Script, "Definitions()\n", 1);

        var process = Assert.Single(_processes);
        Assert.Equal(3, process.Texts.Count);
        Assert.Equal("Definitions()\n", process.Texts[PlaceIn(process, Script)]);
        Assert.Equal("function Definitions() end\n", process.Texts[PlaceIn(process, Machine)]);
        Assert.Equal("function TestValid(wrapper) end\n", process.Texts[PlaceIn(process, Base)]);
        Assert.All(process.Texts.Keys,
            k => Assert.StartsWith("file:///" + process.Plan!.Root.Replace('\\', '/') + "/", k));
    }

    public static TheoryData<string> Setups => new(ProjectSetup.Names);

    // In every shape (#280): the leaf's script and a library only the lowest layer ships, each placed
    // as a script path under the instance's root - the layers keep their roots lower-cased, the editor
    // sends the case on disk, and a file no root matched would land outside the script tree.
    [Theory]
    [MemberData(nameof(Setups))]
    public void AnOpenScript_HoldsALowerLayersLibrary_AtItsScriptPath(string name)
    {
        var setup = ProjectSetup.Named(name);
        var disk = setup.Disk();
        var script = setup.ScriptUri(setup.LeafRank, "GameObject/Interdictor.lua");
        disk.AddFile(setup.PathOf(0, "Scripts/Library/PGStateMachine.lua"),
            new MockFileData("function Definitions() end\n"));
        var index = new FakeGameIndexService
        {
            Current = ProjectSetup.Index(setup.Script(0, "Library/PGStateMachine.lua"),
                setup.Script(setup.LeafRank, "GameObject/Interdictor.lua", ["PGStateMachine"]))
        };
        var processes = new List<FakeProcess>();
        var fileHelper = new FileHelper(disk);
        var instances = new LuaAnalyzerInstances(_ =>
            {
                var process = new FakeProcess();
                processes.Add(process);
                return process;
            }, index, fileHelper, _stubs, new LuaGeneratedTexts([], fileHelper), Directory,
            NullLogger<LuaAnalyzerInstances>.Instance);
        instances.Configure(setup.Layers);

        instances.DidOpen(script, "Definitions()\n", 1);

        var texts = Assert.Single(processes).Texts;
        Assert.Equal(2, texts.Count);
        Assert.Contains(texts, t => t.Key.EndsWith("/gameobject/interdictor.lua", StringComparison.OrdinalIgnoreCase)
                                    && t.Value == "Definitions()\n");
        Assert.Contains(texts, t => t.Key.EndsWith("/library/pgstatemachine.lua", StringComparison.OrdinalIgnoreCase)
                                    && t.Value == "function Definitions() end\n");
        Assert.DoesNotContain(texts.Keys, k => k.Contains("/_outside/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheLibrary_IsTheEngineStubs_PlusTheStubOfARequiredLibraryNoLayerShips()
    {
        Index((Script, ["PGStateMachine", "PGDebug"], 1), (Machine, ["PGBase"], 0), (Base, [], 0));
        var instances = Instances();
        instances.Configure(Layers);

        instances.DidOpen(Script, "", 1);

        Assert.Equal(["C:/schema/lua/api.d.lua", "C:/schema/lua/globals.d.lua", "C:/schema/lua/pgdebug.d.lua"],
            _processes.Single().Plan!.Library.Select(p => p.Replace('\\', '/')));
    }

    [Fact]
    public void TwoOpenScripts_TwoAnalyzers_NeitherHoldsTheOther()
    {
        var instances = Instances();
        instances.Configure(Layers);

        instances.DidOpen(Script, "", 1);
        instances.DidOpen(Other, "transport = 5\n", 1);

        Assert.Equal(2, _processes.Count);
        Assert.Equal(3, _processes[0].Texts.Count);
        Assert.Single(_processes[1].Texts);
    }

    [Fact]
    public void AFileReadFromDisk_IsSentWithTheAnnotations_TheOpenScriptAsTheEditorSentIt()
    {
        var instances = Instances(new FirstLineAnnotation());
        instances.Configure(Layers);

        instances.DidOpen(Script, "Definitions()\n", 1);

        var process = _processes.Single();
        Assert.Equal("---@generated\nfunction Definitions() end\n", process.Texts[PlaceIn(process, Machine)]);
        Assert.Equal("Definitions()\n", process.Texts[PlaceIn(process, Script)]);
    }

    [Fact]
    public void AnEditToAnOpenFile_ReachesEveryInstanceHoldingIt()
    {
        var instances = Instances();
        instances.Configure(Layers);
        instances.DidOpen(Script, "", 1);
        instances.DidOpen(Machine, "function Definitions() end\n", 1);

        instances.DidChange(Machine, "function Definitions(x) end\n", 2);

        Assert.All(_processes, p => Assert.Equal("function Definitions(x) end\n", p.Texts[PlaceIn(p, Machine)]));
    }

    [Fact]
    public void Closing_StopsTheScriptsAnalyzer_AndAFileStillHeldElsewhere_GoesBackToItsTextOnDisk()
    {
        var instances = Instances();
        instances.Configure(Layers);
        instances.DidOpen(Script, "", 1);
        instances.DidOpen(Machine, "unsaved\n", 1);

        instances.DidClose(Machine);

        Assert.True(_processes[1].Disposed);
        Assert.False(_processes[0].Disposed);
        Assert.Equal("function Definitions() end\n", _processes[0].Texts[PlaceIn(_processes[0], Machine)]);
    }

    [Fact]
    public async Task ARequest_GoesToTheRequestingScriptsAnalyzer_InItsPlaces_AndTheAnswerLeadsBack()
    {
        var instances = Instances();
        instances.Configure(Layers);
        instances.DidOpen(Script, "", 1);
        instances.DidOpen(Other, "", 1);
        var process = _processes[0];
        process.Answer = (_, _) => JToken.FromObject(new[]
        {
            new
            {
                uri = PlaceIn(process, Base),
                range = new { start = new { line = 0, character = 9 }, end = new { line = 0, character = 18 } }
            },
            new
            {
                uri = "file:///C:/schema/lua/api.d.lua",
                range = new { start = new { line = 3, character = 0 }, end = new { line = 3, character = 1 } }
            }
        });

        var answer = await instances.RequestAsync<JArray>("textDocument/definition",
            new DefinitionParams
            {
                TextDocument = new TextDocumentIdentifier(DocumentUri.From(Script)),
                Position = new Position(0, 2)
            }, CancellationToken.None);

        Assert.Equal(PlaceIn(process, Script),
            process.Requests.Single().Parameters["textDocument"]!["uri"]!.Value<string>());
        Assert.Empty(_processes[1].Requests);
        Assert.Equal([Base, "file:///C:/schema/lua/api.d.lua"], answer!.Select(l => l["uri"]!.Value<string>()));
    }

    // A lens names its declaration alone - {"DeclId": "0|2074"}, measured on FoC - so its resolve
    // carries no document and would reach no analyzer.
    [Fact]
    public async Task ALensResolve_GoesToTheAnalyzerThatMadeTheLens_WithTheLensItMade()
    {
        var instances = Instances();
        instances.Configure(Layers);
        instances.DidOpen(Script, "", 1);
        instances.DidOpen(Other, "", 1);
        var process = _processes[0];
        var range = new { start = new { line = 0, character = 9 }, end = new { line = 0, character = 18 } };
        process.Answer = (m, payload) => m == "textDocument/codeLens"
            ? JToken.FromObject(new[] { new { range, data = new { DeclId = "0|2074" } } })
            : new JObject
            {
                ["range"] = payload["range"], ["data"] = payload["data"],
                ["command"] = new JObject { ["title"] = "1 usage", ["command"] = "editor.action.showReferences" }
            };

        var lenses = await instances.RequestAsync<JArray>("textDocument/codeLens",
            new CodeLensParams { TextDocument = new TextDocumentIdentifier(DocumentUri.From(Script)) },
            CancellationToken.None);
        var resolved = await instances.RequestAsync<JObject>("codeLens/resolve", lenses!.Single(),
            CancellationToken.None);

        var (method, sent) = process.Requests[^1];
        Assert.Equal("codeLens/resolve", method);
        Assert.True(JToken.DeepEquals(new JObject { ["DeclId"] = "0|2074" }, sent["data"]));
        Assert.Empty(_processes[1].Requests);
        Assert.Equal("1 usage", resolved!["command"]!["title"]!.Value<string>());
    }

    [Fact]
    public void TheScriptsFindings_ReachOutUnderItsOwnUri_FindingsOnTheFilesItRequiresDoNot()
    {
        var instances = Instances();
        var published = new List<PublishDiagnosticsParams>();
        instances.DiagnosticsPublished += published.Add;
        instances.Configure(Layers);
        instances.DidOpen(Script, "", 1);
        var process = _processes.Single();
        var finding = new Diagnostic { Message = "unused", Range = new Range(0, 0, 0, 1) };

        process.Publish(PlaceIn(process, Machine), finding);
        process.Publish(PlaceIn(process, Script), finding);

        var only = Assert.Single(published);
        Assert.True(DocumentUris.Same(Script, new FileHelper(_disk).NormalizeUri(only.Uri.ToString())));
        Assert.Equal("unused", only.Diagnostics.Single().Message);
    }

    [Fact]
    public void ANewlyRequiredFile_IsSent_AndTheStubItReplaces_LeavesTheLibrary()
    {
        Index((Script, ["PGStateMachine", "PGDebug"], 1), (Machine, ["PGBase"], 0), (Base, [], 0));
        var instances = Instances();
        instances.Configure(Layers);
        instances.DidOpen(Script, "", 1);
        var process = _processes.Single();

        Index((Script, ["PGStateMachine", "PGDebug"], 1), (Machine, ["PGBase"], 0), (Base, [], 0), (Debug, [], 0));
        _index.Raise(_index.Current);

        Assert.True(SpinWait.SpinUntil(() => process.Texts.Count == 4, TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(() => process.Plans.Count == 2, TimeSpan.FromSeconds(5)));
        Assert.DoesNotContain(process.Plan!.Library, p => p.EndsWith("pgdebug.d.lua", StringComparison.Ordinal));
    }

    [Fact]
    public void AFileChangedOnDisk_IsSentAgain()
    {
        var instances = Instances();
        instances.Configure(Layers);
        instances.DidOpen(Script, "", 1);
        var process = _processes.Single();

        _disk.File.WriteAllText(new FileHelper(_disk).FileUriToPath(Base)!, "function TestValid(w, x) end\n");
        Index((Script, ["PGStateMachine"], 1), (Machine, ["PGBase"], 0), (Base, [], 0), (Other, [], 1));
        _index.Raise(_index.Current);

        Assert.True(SpinWait.SpinUntil(() => process.Texts[PlaceIn(process, Base)] == "function TestValid(w, x) end\n",
            TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void BeforeTheProjectIsKnown_NothingStarts_ThenEveryOpenScriptDoes()
    {
        var instances = Instances();

        instances.DidOpen(Script, "", 1);
        Assert.False(instances.IsRunning);
        Assert.Empty(_processes);

        instances.Configure(Layers);

        Assert.True(instances.IsRunning);
        Assert.Single(_processes);
    }

    // A server that ends without disposing leaves its directory behind (measured: 123 of them).
    [Fact]
    public void TheDirectoriesOfServersNoLongerRunning_AreRemoved_AndItsOwnOnStop()
    {
        _disk.AddFile("C:/cache/4242/1/emmyrc.json", new MockFileData("{}"));
        _disk.AddFile("C:/cache/77/1/emmyrc.json", new MockFileData("{}"));
        var fileHelper = new FileHelper(_disk);
        var instances = new LuaAnalyzerInstances(_ => new FakeProcess(), _index, fileHelper, _stubs,
            new LuaGeneratedTexts([], fileHelper), "C:/cache/77", NullLogger<LuaAnalyzerInstances>.Instance,
            pid => pid == 77);

        instances.Configure(Layers);
        Assert.False(_disk.Directory.Exists("C:/cache/4242"));
        Assert.True(_disk.Directory.Exists("C:/cache/77"));

        instances.Dispose();
        Assert.False(_disk.Directory.Exists("C:/cache/77"));
    }

    // ── doubles ──────────────────────────────────────────────────────────────

    private sealed class FirstLineAnnotation : ILuaAnnotationSource
    {
        public IEnumerable<LuaAnnotationInsertion> For(SyntaxTree tree, string uri)
        {
            return [new LuaAnnotationInsertion(0, ["---@generated"])];
        }
    }

    // ── lifetime ─────────────────────────────────────────────────────────────

    // The analyzer reads a file written under its folder as a change of its own; with the edit sent
    // as well it reported nothing on 17 of 24 edits of a vanilla script (measured, 0.25.1). The copy
    // on disk is for its startup scan only.
    [Fact]
    public void AnEditToAnOpenScript_IsSent_AndNotWrittenToTheInstancesCopy()
    {
        var instances = Instances();
        instances.Configure(Layers);
        instances.DidOpen(Script, "Definitions()\n", 1);
        var process = _processes.Single();
        var place = PlaceIn(process, Script);

        instances.DidChange(Script, "Definitions()\nx = \"broken\n", 2);

        Assert.Equal("Definitions()\nx = \"broken\n", process.Texts[place]);
        Assert.Equal("Definitions()\n", _disk.File.ReadAllText(new FileHelper(_disk).FileUriToPath(place)!));
    }

    private static readonly LuaAnalyzerInstanceTiming Timing =
        new(TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(60));

    // The editor opens a file for a moment to show a hover or a peek of it, and closes it again: an
    // analyzer started for that was killed while it started (34 starts in one measured session).
    [Fact]
    public void AScriptOpenedAndClosedAgainAtOnce_StartsNoAnalyzer()
    {
        var time = new FakeTimeProvider();
        var instances = Instances(Timing, time);
        instances.Configure(Layers);

        instances.DidOpen(Script, "", 1);
        instances.DidClose(Script);
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Empty(_processes);
    }

    [Fact]
    public void AScriptThatStaysOpen_GetsItsAnalyzer_OnceItSettled()
    {
        var time = new FakeTimeProvider();
        var instances = Instances(Timing, time);
        instances.Configure(Layers);

        instances.DidOpen(Script, "Definitions()\n", 1);
        Assert.Empty(_processes);
        time.Advance(TimeSpan.FromMilliseconds(250));

        var process = Assert.Single(_processes);
        Assert.Equal("Definitions()\n", process.Texts[PlaceIn(process, Script)]);
    }

    [Fact]
    public void AClosedScriptsAnalyzer_IsKeptForTheGrace_AndServesTheScriptReopened()
    {
        var time = new FakeTimeProvider();
        var instances = Instances(Timing, time);
        instances.Configure(Layers);
        instances.DidOpen(Script, "Definitions()\n", 1);
        time.Advance(TimeSpan.FromMilliseconds(250));

        instances.DidClose(Script);
        time.Advance(TimeSpan.FromSeconds(59));
        instances.DidOpen(Script, "Definitions()\nx = 1\n", 2);
        time.Advance(TimeSpan.FromSeconds(120));

        var process = Assert.Single(_processes);
        Assert.False(process.Disposed);
        Assert.Equal("Definitions()\nx = 1\n", process.Texts[PlaceIn(process, Script)]);
    }

    [Fact]
    public void AClosedScriptsAnalyzer_StopsAfterTheGrace()
    {
        var time = new FakeTimeProvider();
        var instances = Instances(Timing, time);
        instances.Configure(Layers);
        instances.DidOpen(Script, "", 1);
        time.Advance(TimeSpan.FromMilliseconds(250));

        instances.DidClose(Script);
        time.Advance(TimeSpan.FromSeconds(60));

        Assert.True(Assert.Single(_processes).Disposed);
    }

    // A closed script's findings leave with it: the analyzer kept for its reopening says nothing.
    [Fact]
    public void AClosedScriptsAnalyzer_PublishesNothing_WhileItIsKept()
    {
        var time = new FakeTimeProvider();
        var instances = Instances(Timing, time);
        var published = new List<PublishDiagnosticsParams>();
        instances.DiagnosticsPublished += published.Add;
        instances.Configure(Layers);
        instances.DidOpen(Script, "", 1);
        time.Advance(TimeSpan.FromMilliseconds(250));
        var process = _processes.Single();
        var place = PlaceIn(process, Script);

        instances.DidClose(Script);
        process.Publish(place, new Diagnostic { Message = "late" });

        Assert.Empty(published);
    }

    [Fact]
    public void Disposing_StopsTheAnalyzersKeptForAReopening()
    {
        var time = new FakeTimeProvider();
        var instances = Instances(Timing, time);
        instances.Configure(Layers);
        instances.DidOpen(Script, "", 1);
        time.Advance(TimeSpan.FromMilliseconds(250));
        instances.DidClose(Script);

        instances.Dispose();

        Assert.True(Assert.Single(_processes).Disposed);
    }

    private sealed class FakeProcess : ILuaAnalyzerProcess
    {
        public Dictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);
        public List<LuaAnalyzerInstancePlan> Plans { get; } = [];
        public LuaAnalyzerInstancePlan? Plan => Plans.Count == 0 ? null : Plans[^1];
        public List<(string Method, JToken Parameters)> Requests { get; } = [];
        public Func<string, JToken, JToken?> Answer { get; set; } = (_, _) => null;
        public bool Disposed { get; private set; }

        public bool IsRunning => Plans.Count > 0 && !Disposed;
        public event Action<PublishDiagnosticsParams>? DiagnosticsPublished;

        public void DidOpen(string uri, string text, int version)
        {
            lock (Texts) Texts[uri] = text;
        }

        public void DidChange(string uri, string text, int version)
        {
            lock (Texts) Texts[uri] = text;
        }

        public void DidClose(string uri)
        {
            lock (Texts) Texts.Remove(uri);
        }

        public Task<T?> RequestAsync<T>(string method, object parameters, CancellationToken ct) where T : class?
        {
            var json = JToken.FromObject(parameters);
            Requests.Add((method, json));
            return Task.FromResult(Answer(method, json)?.ToObject<T>());
        }

        public Task StartAsync(LuaAnalyzerInstancePlan plan)
        {
            lock (Plans) Plans.Add(plan);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            Disposed = true;
        }

        public void Publish(string uri, Diagnostic diagnostic)
        {
            DiagnosticsPublished?.Invoke(new PublishDiagnosticsParams
            {
                Uri = DocumentUri.From(uri), Diagnostics = new Container<Diagnostic>(diagnostic)
            });
        }
    }
}