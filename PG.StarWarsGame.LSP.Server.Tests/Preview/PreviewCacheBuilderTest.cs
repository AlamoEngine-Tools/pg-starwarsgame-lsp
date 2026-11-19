// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;
using PG.StarWarsGame.LSP.Server.Tests.Assets;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The background build of the preview cache (#436): every model of the project converted once
///     its first index is live, shown, cancellable, and reordered for a subject the reader opens.
/// </summary>
public sealed class PreviewCacheBuilderTest
{
    private const string ArtRoot = "/mod/data/art";
    private const string PreviewDir = "/mod/.aetswg/preview";
    private const string Marker = PreviewDir + "/" + PreviewCacheBuilder.MarkerFileName;

    private sealed class Rig
    {
        public MockFileSystem Files { get; } = new();
        public RecordingCache Cache { get; } = new();
        public CountingConverter Converter { get; } = new();
        public ShownProgress Progress { get; } = new();
        public string? Directory { get; set; } = PreviewDir;
        public List<string> ParticleSystems { get; } = [];
        public bool Project { get; set; } = true;
        public Dictionary<string, string[]> Archives { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Runs once, the next time the builder asks where the cache is.</summary>
        public Action? OnNextDirectoryRead { get; set; }

        public Rig With(params string[] files)
        {
            foreach (var file in files)
                Files.AddFile(ArtRoot + "/models/" + file, new MockFileData([1]));
            return this;
        }

        public PreviewCacheBuilder Build()
        {
            var assetFiles = Files.AllFiles
                .Select(f => f.Replace('\\', '/'))
                .Where(f => f.Contains("/data/art/", StringComparison.OrdinalIgnoreCase))
                .Select(f => f[(f.IndexOf("/data/art/", StringComparison.OrdinalIgnoreCase) + 1)..].ToLowerInvariant())
                .ToList();
            var index = GameIndex.Empty with
            {
                AssetFiles = MergedAssetFileIndex.Merge([], assetFiles),
                ParticleSystems = ParticleSystems.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase)
            };
            var config = Project
                ? new WorkspaceConfiguration([], [], [], [ArtRoot], null)
                {
                    Layers =
                    [
                        new ProjectLayer(0, "core", [], [], [], ["/core/data/art"], null, "/core/core.pgproj"),
                        new ProjectLayer(1, "mod", [], [], [], [ArtRoot], null, "/mod/mod.pgproj")
                    ]
                }
                : null;

            return new PreviewCacheBuilder(new FakeGameIndexService(index), new StubProjects(config), Cache,
                Converter, new FixedLocation(ReadDirectory), new FileHelper(Files), new ArchiveEntries(Archives),
                Progress,
                NullLogger<PreviewCacheBuilder>.Instance);
        }

        private string? ReadDirectory()
        {
            var read = OnNextDirectoryRead;
            OnNextDirectoryRead = null;
            read?.Invoke();
            return Directory;
        }
    }

    private sealed class ArchiveEntries(Dictionary<string, string[]> archives) : IMegEntryReader
    {
        public IEnumerable<string> EnumerateEntryPaths(string megFilePath)
        {
            return archives.GetValueOrDefault(megFilePath) ?? [];
        }
    }

    private static async Task Finished(PreviewCacheBuilder builder)
    {
        var idle = builder.Idle;
        var done = await Task.WhenAny(idle,
            Task.Delay(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));
        Assert.Same(idle, done);
    }

    // ── what is built ─────────────────────────────────────────────────────────

    [Fact]
    public async Task StartUnlessBuilt_ConvertsEveryModelOfTheProject_EachWithTheClipsAPlainOpenOffers()
    {
        var rig = new Rig().With("Hull.alo", "Hull_idle_00.ala", "Hull_die_00.ala", "Turret.alo", "P_smoke.alo");
        rig.ParticleSystems.Add("P_smoke.alo");
        rig.Files.AddFile("/core/data/art/models/Core_only.alo", new MockFileData([1]));
        var builder = rig.Build();

        builder.StartUnlessBuilt();
        await Finished(builder);

        // The project's own two models: not the particle system, not the dependency's model. The
        // one with clips twice - with them, as a subject opens it, and without, as a unit that
        // attaches it as a part asks for it when none of the unit's own clips plays on it.
        Assert.Equal(
        [
            "Data/Art/Models/Hull.alo <- Hull_die_00.ala,Hull_idle_00.ala",
            "Data/Art/Models/Hull.alo <- ",
            "Data/Art/Models/Turret.alo <- "
        ], rig.Cache.Calls.Select(c => c.Replace("hull_", "Hull_")));
    }

    [Fact]
    public async Task StartUnlessBuilt_ShowsItsProgress_AndEndsIt()
    {
        var rig = new Rig().With("A.alo", "B.alo", "C.alo");
        var builder = rig.Build();

        builder.StartUnlessBuilt();
        await Finished(builder);

        Assert.Equal(3, rig.Progress.Total);
        Assert.Equal([(1, 3), (2, 3), (3, 3)], rig.Progress.Reports);
        Assert.True(rig.Progress.Ended);
    }

    // ── once ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ABuildThatRanToItsEnd_LeavesAMarker_AndDoesNotStartAgain()
    {
        var rig = new Rig().With("A.alo");
        var builder = rig.Build();
        builder.StartUnlessBuilt();
        await Finished(builder);

        Assert.True(rig.Files.File.Exists(Marker));

        var later = rig.Build();
        later.StartUnlessBuilt();
        await Finished(later);

        Assert.Single(rig.Cache.Calls);
    }

    [Fact]
    public async Task AMarkerOfAnotherConverterVersion_IsNotThisBuild()
    {
        var rig = new Rig().With("A.alo");
        rig.Files.AddFile(Marker, new MockFileData("converter 0\n"));
        var builder = rig.Build();

        builder.StartUnlessBuilt();
        await Finished(builder);

        Assert.Single(rig.Cache.Calls);
        Assert.Equal($"converter {PreviewModelCache.ConverterVersion}", rig.Files.File.ReadAllText(Marker).Trim());
    }

    /// <summary>The cleared cache has no marker either, so the next first index builds again.</summary>
    [Fact]
    public async Task AProjectWithNoModels_IsBuiltAtOnce()
    {
        var rig = new Rig();
        var builder = rig.Build();

        builder.StartUnlessBuilt();
        await Finished(builder);

        Assert.True(rig.Files.File.Exists(Marker));
        Assert.Empty(rig.Cache.Calls);
        Assert.Null(rig.Progress.Total);
    }

    [Fact]
    public async Task StartUnlessBuilt_WithNoProject_DoesNothing()
    {
        var rig = new Rig { Project = false, Directory = null }.With("A.alo");
        var builder = rig.Build();

        builder.StartUnlessBuilt();
        await Finished(builder);

        Assert.Empty(rig.Cache.Calls);
        Assert.False(rig.Files.Directory.Exists(PreviewDir));
    }

    // ── cancelled ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelled_ItStopsAfterTheModelInHand_LeavesNoMarker_AndTheNextStartResumes()
    {
        var rig = new Rig().With("A.alo", "B.alo", "C.alo");
        rig.Cache.HoldFirst();
        var builder = rig.Build();
        builder.StartUnlessBuilt();
        await rig.Cache.FirstEntered;

        builder.Cancel();
        rig.Cache.Release();
        await Finished(builder);

        Assert.Single(rig.Cache.Calls);
        Assert.False(rig.Files.File.Exists(Marker));
        Assert.True(rig.Progress.Ended);

        var later = rig.Build();
        later.StartUnlessBuilt();
        await Finished(later);

        // All three are walked again; the cache answers for the one already converted.
        Assert.Equal(4, rig.Cache.Calls.Count);
        Assert.True(rig.Files.File.Exists(Marker));
    }

    /// <summary>
    ///     A build the reader cancelled is over. A model saved afterwards is converted again like any
    ///     other save - not shown as a build, and not taken for the end of the one that was
    ///     cancelled: its marker would keep every later session from building the project.
    /// </summary>
    [Fact]
    public async Task Cancelled_ALaterSaveIsNotShownAsABuild_AndLeavesNoMarker()
    {
        var rig = new Rig().With("A.alo", "B.alo", "C.alo");
        rig.Cache.HoldFirst();
        var builder = rig.Build();
        builder.StartUnlessBuilt();
        await rig.Cache.FirstEntered;
        builder.Cancel();
        rig.Cache.Release();
        await Finished(builder);

        builder.FilesChanged(["/mod/data/art/models/B.alo"]);
        await Finished(builder);

        Assert.Equal(2, rig.Cache.Calls.Count);
        Assert.Equal(1, rig.Progress.Begins);
        Assert.False(rig.Files.File.Exists(Marker));
    }

    // ── an opened subject goes first ──────────────────────────────────────────

    [Fact]
    public void TheProgress_IsToldToTheClient_RunningFromTheStartAndEndedAtTheEnd()
    {
        var sent = new List<PreviewCacheBuildProgress>();
        var progress = new NotifiedPreviewBuildProgress(sent.Add, NullLogger<NotifiedPreviewBuildProgress>.Instance);

        using (var run = progress.Begin(3, () => { }))
        {
            run.Report(1, 3);
            run.Report(2, 4);
        }

        Assert.Equal(
        [
            new PreviewCacheBuildProgress("running", 0, 3),
            new PreviewCacheBuildProgress("running", 1, 3),
            new PreviewCacheBuildProgress("running", 2, 4),
            new PreviewCacheBuildProgress("ended", 2, 4)
        ], sent);
    }

    [Fact]
    public async Task TheCancelRequest_StopsTheBuild()
    {
        var rig = new Rig().With("A.alo", "B.alo");
        rig.Cache.HoldFirst();
        var builder = rig.Build();
        builder.StartUnlessBuilt();
        await rig.Cache.FirstEntered;

        await new CancelPreviewCacheBuildHandler(builder).Handle(new CancelPreviewCacheBuildParams(),
            CancellationToken.None);
        rig.Cache.Release();
        await Finished(builder);

        Assert.Single(rig.Cache.Calls);
    }

    /// <summary>
    ///     FR-11: the preview is never held back for the build; the build is reordered for the
    ///     preview. The subject's models go to the front in the order given, including ones the
    ///     queue did not hold - a dependency's hull, a death clone with clips of its own.
    /// </summary>
    [Fact]
    public async Task Prioritise_PutsTheOpenedSubjectsModelsFirst_AndAddsOnesTheQueueDidNotHold()
    {
        var rig = new Rig().With("A.alo", "B.alo", "C.alo", "D.alo");
        rig.Cache.HoldFirst();
        var builder = rig.Build();
        builder.StartUnlessBuilt();
        await rig.Cache.FirstEntered;

        builder.Prioritise(
        [
            new PreviewBuildItem("D.alo", []),
            new PreviewBuildItem("Core_hull.alo", ["Core_hull_die_00.ala"]),
            // Being converted this moment: left where it is.
            new PreviewBuildItem("a.ALO", [])
        ]);
        rig.Cache.Release();
        await Finished(builder);

        Assert.Equal(
        [
            "Data/Art/Models/A.alo <- ",
            "Data/Art/Models/D.alo <- ",
            "Data/Art/Models/Core_hull.alo <- Core_hull_die_00.ala",
            "Data/Art/Models/B.alo <- ",
            "Data/Art/Models/C.alo <- "
        ], rig.Cache.Calls);
        Assert.Equal((5, 5), rig.Progress.Reports[^1]);
        Assert.True(rig.Files.File.Exists(Marker));
    }

    /// <summary>With no build under way a request converts what it needs; nothing is queued for it.</summary>
    [Fact]
    public async Task Prioritise_WithNoBuildUnderWay_DoesNothing()
    {
        var rig = new Rig().With("A.alo");
        rig.Files.AddFile(Marker, new MockFileData($"converter {PreviewModelCache.ConverterVersion}\n"));
        var builder = rig.Build();

        builder.Prioritise([new PreviewBuildItem("A.alo", [])]);
        await Finished(builder);

        Assert.Empty(rig.Cache.Calls);
    }

    // ── clearing ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     The clear request stops a build under way before it empties the cache (FR-9): one still
    ///     running would fill the folder again behind the command.
    /// </summary>
    [Fact]
    public async Task TheClearRequest_StopsTheBuild_EmptiesTheCache_AndSaysHowManyEntriesWent()
    {
        var rig = new Rig().With("A.alo", "B.alo", "C.alo");
        rig.Cache.HoldFirst();
        var builder = rig.Build();
        builder.StartUnlessBuilt();
        await rig.Cache.FirstEntered;

        var result = await new ClearPreviewCacheHandler(builder, rig.Cache)
            .Handle(new ClearPreviewCacheParams(), TestContext.Current.CancellationToken);
        rig.Cache.Release();
        await Finished(builder);

        Assert.Equal(3, result.Removed);
        Assert.Equal(1, rig.Cache.Cleared);
        Assert.Single(rig.Cache.Calls);
        Assert.False(rig.Files.File.Exists(Marker));
    }

    // ── changed on disk ───────────────────────────────────────────────────────

    [Fact]
    public async Task Requeue_ConvertsTheNamedModelsInTheBackground_WithoutAFullBuild()
    {
        var rig = new Rig().With("A.alo", "B.alo");
        rig.Files.AddFile(Marker, new MockFileData($"converter {PreviewModelCache.ConverterVersion}\n"));
        var builder = rig.Build();

        builder.Requeue([new PreviewBuildItem("B.alo", ["B_idle_00.ala"])]);
        await Finished(builder);

        Assert.Equal(["Data/Art/Models/B.alo <- B_idle_00.ala"], rig.Cache.Calls);
    }

    /// <summary>
    ///     A model saved in the moment the worker leaves - after it found its queue empty, before it
    ///     has gone - is still converted. The window is held open here by the one thing the worker
    ///     does inside it: ending the progress it showed.
    /// </summary>
    [Fact]
    public async Task Requeue_AsTheWorkerLeaves_IsStillConverted()
    {
        var rig = new Rig().With("A.alo");
        var builder = rig.Build();
        rig.Progress.OnEnded = () => builder.Requeue([new PreviewBuildItem("Late.alo", [])]);

        builder.StartUnlessBuilt();
        await Finished(builder);

        Assert.Equal(["Data/Art/Models/A.alo <- ", "Data/Art/Models/Late.alo <- "], rig.Cache.Calls);

        // The build of every model did run to its end, and the late one is no build to show.
        Assert.True(rig.Files.File.Exists(Marker));
        Assert.Equal(1, rig.Progress.Begins);
    }

    /// <summary>
    ///     A cancel that lands after one worker has left and before the next has taken up what was
    ///     queued behind it stops that next one all the same: nothing it would have converted is
    ///     converted. The moment is held open by what the leaving worker does in it - writing the
    ///     marker of the build it finished, for which it asks where the cache is.
    /// </summary>
    [Fact]
    public async Task Cancel_BetweenAWorkerLeavingAndTheNextStarting_StopsTheNext()
    {
        var rig = new Rig().With("A.alo");
        var builder = rig.Build();
        rig.Progress.OnEnded = () =>
        {
            builder.Requeue([new PreviewBuildItem("Late.alo", [])]);
            rig.OnNextDirectoryRead = builder.Cancel;
        };

        builder.StartUnlessBuilt();
        await Finished(builder);

        Assert.Equal(["Data/Art/Models/A.alo <- "], rig.Cache.Calls);
        Assert.True(rig.Files.File.Exists(Marker));

        // And the builder is not left thinking a worker runs: the next save is converted.
        builder.Requeue([new PreviewBuildItem("Next.alo", [])]);
        await Finished(builder);
        Assert.Equal(["Data/Art/Models/A.alo <- ", "Data/Art/Models/Next.alo <- "], rig.Cache.Calls);
    }

    /// <summary>A save reported while the server is shutting down starts no worker.</summary>
    [Fact]
    public async Task Requeue_AfterTheBuilderWasDisposed_StartsNothing()
    {
        var rig = new Rig().With("A.alo");
        var builder = rig.Build();
        builder.Dispose();

        builder.Requeue([new PreviewBuildItem("A.alo", [])]);
        await Finished(builder);

        Assert.Empty(rig.Cache.Calls);
    }

    [Fact]
    public async Task Requeue_WithNoProject_DoesNothing()
    {
        var rig = new Rig { Project = false, Directory = null };
        var builder = rig.Build();

        builder.Requeue([new PreviewBuildItem("B.alo", [])]);
        await Finished(builder);

        Assert.Empty(rig.Cache.Calls);
    }

    /// <summary>A model written to disk is converted again, with the clips a plain open offers for it (FR-12).</summary>
    [Fact]
    public async Task FilesChanged_ForAModel_ConvertsItAgainWithItsClips()
    {
        var rig = new Rig().With("Hull.alo", "Hull_idle_00.ala", "Turret.alo");
        var builder = rig.Build();

        builder.FilesChanged([@"\mod\data\art\models\Hull.alo"]);
        await Finished(builder);

        Assert.Equal(["Data/Art/Models/Hull.alo <- hull_idle_00.ala"], rig.Cache.Calls);
    }

    /// <summary>A clip is part of every model that offers it: those are converted again, the others are not.</summary>
    [Fact]
    public async Task FilesChanged_ForAClip_ConvertsTheModelsThatOfferIt()
    {
        var rig = new Rig().With("Hull.alo", "Hull_idle_00.ala", "Hull_die_00.ala", "Turret.alo", "Turret_idle_00.ala");
        var builder = rig.Build();

        builder.FilesChanged(["/mod/data/art/models/Hull_die_00.ala"]);
        await Finished(builder);

        Assert.Equal(["Data/Art/Models/hull.alo <- hull_die_00.ala,hull_idle_00.ala"], rig.Cache.Calls);
    }

    /// <summary>A changed archive is a change to every model in it, and to the models its clips belong to.</summary>
    [Fact]
    public async Task FilesChanged_ForAnArchive_ConvertsTheModelsItHolds()
    {
        var rig = new Rig().With("Hull.alo", "Hull_idle_00.ala", "Turret.alo", "Other.alo");
        rig.Archives["/mod/data/Models.meg"] =
        [
            @"DATA\ART\MODELS\PACKED.ALO", @"DATA\ART\MODELS\TURRET.ALO", @"DATA\ART\MODELS\HULL_IDLE_00.ALA",
            @"DATA\ART\TEXTURES\SKIN.DDS"
        ];
        var builder = rig.Build();

        builder.FilesChanged(["/mod/data/Models.meg"]);
        await Finished(builder);

        Assert.Equal(
        [
            "Data/Art/Models/hull.alo <- hull_idle_00.ala",
            "Data/Art/Models/PACKED.ALO <- ",
            "Data/Art/Models/TURRET.ALO <- "
        ], rig.Cache.Calls);
    }

    [Fact]
    public async Task FilesChanged_ForATextureOrAParticleSystem_ConvertsNothing()
    {
        var rig = new Rig().With("Hull.alo", "P_smoke.alo");
        rig.ParticleSystems.Add("P_smoke.alo");
        var builder = rig.Build();

        builder.FilesChanged(["/mod/data/art/textures/Skin.dds", "/mod/data/art/models/P_smoke.alo"]);
        await Finished(builder);

        Assert.Empty(rig.Cache.Calls);
    }

    /// <summary>
    ///     What is shown, and can be cancelled, is the build of every model. A file saved to disk is
    ///     converted again without a word: a notification per save would be noise.
    /// </summary>
    [Fact]
    public async Task FilesChanged_IsNotShownAsABuild()
    {
        var rig = new Rig().With("Hull.alo");
        var builder = rig.Build();

        builder.FilesChanged(["/mod/data/art/models/Hull.alo"]);
        await Finished(builder);

        Assert.Single(rig.Cache.Calls);
        Assert.Null(rig.Progress.Total);
    }

    // ── a model that will not convert ─────────────────────────────────────────

    [Fact]
    public async Task AModelThatWillNotConvert_DoesNotCostTheRestTheirPlace()
    {
        var rig = new Rig().With("A.alo", "Broken.alo", "Not_a_model.alo", "Z.alo");
        rig.Cache.Throws["Data/Art/Models/Broken.alo"] = new InvalidOperationException("boom");
        rig.Cache.Throws["Data/Art/Models/Not_a_model.alo"] = new AloFormatException("no skeleton");
        var builder = rig.Build();

        builder.StartUnlessBuilt();
        await Finished(builder);

        Assert.Equal(4, rig.Cache.Calls.Count);
        Assert.Equal((4, 4), rig.Progress.Reports[^1]);
        Assert.True(rig.Files.File.Exists(Marker));
    }

    /// <summary>The build converts with the server's one converter, the same a request converts with.</summary>
    [Fact]
    public async Task TheBuild_ConvertsWithTheConverterItWasGiven()
    {
        var rig = new Rig().With("A.alo");
        var builder = rig.Build();

        builder.StartUnlessBuilt();
        await Finished(builder);
        rig.Cache.LastConvert!([1], []);
        rig.Cache.LastJudge!([1], []);

        Assert.Equal((1, 1), (rig.Converter.Converted, rig.Converter.Judged));
    }

    private sealed class CountingConverter : IModelGlbConverter
    {
        public int Converted { get; private set; }
        public int Judged { get; private set; }

        public PreviewModelConversion Convert(byte[] aloBytes, IReadOnlyList<PreviewClipSource> offered)
        {
            Converted++;
            return new PreviewModelConversion([], [], []);
        }

        public IReadOnlyList<PreviewClipVerdict> Judge(byte[] aloBytes, IReadOnlyList<PreviewClipSource> offered)
        {
            Judged++;
            return [];
        }
    }

    private sealed class FixedLocation(Func<string?> directory) : IPreviewCacheLocation
    {
        public string? Directory => directory();

        public void EnsureIgnored()
        {
        }
    }

    /// <summary>The cache as a list of what it was asked to convert, able to hold the first request.</summary>
    private sealed class RecordingCache : IPreviewModelCache
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(true);
        private bool _hold;

        public List<string> Calls { get; } = [];
        public Dictionary<string, Exception> Throws { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Task FirstEntered => _entered.Task;
        public int Cleared { get; private set; }
        public PreviewModelConverter? LastConvert { get; private set; }
        public PreviewClipJudge? LastJudge { get; private set; }

        public int Clear()
        {
            Cleared++;
            return 3;
        }

        public PreviewCachedModel? GetOrConvert(string modelPath, IReadOnlyList<(string Name, string Path)> clips,
            PreviewModelConverter convert, PreviewClipJudge judge, string? have = null)
        {
            lock (Calls)
            {
                Calls.Add(modelPath + " <- " + string.Join(",", clips.Select(c => c.Name)));
                LastConvert = convert;
                LastJudge = judge;
            }

            if (_hold)
            {
                _hold = false;
                _entered.TrySetResult();
                _release.Wait(TimeSpan.FromSeconds(20));
            }

            return Throws.TryGetValue(modelPath, out var failure) ? throw failure : null;
        }

        public void HoldFirst()
        {
            _hold = true;
            _release.Reset();
        }

        public void Release()
        {
            _release.Set();
        }
    }

    private sealed class ShownProgress : IPreviewBuildProgress
    {
        private Action? _cancel;

        public int? Total { get; private set; }
        public int Begins { get; private set; }
        public List<(int Done, int Total)> Reports { get; } = [];
        public bool Ended { get; private set; }

        /// <summary>Runs once, when the first run is ended.</summary>
        public Action? OnEnded { get; set; }

        public IPreviewBuildProgressRun Begin(int total, Action cancel)
        {
            Total = total;
            Begins++;
            _cancel = cancel;
            Ended = false;
            return new Run(this);
        }

        public void Cancel()
        {
            _cancel?.Invoke();
        }

        private sealed class Run(ShownProgress owner) : IPreviewBuildProgressRun
        {
            public void Report(int done, int total)
            {
                owner.Reports.Add((done, total));
            }

            public void Dispose()
            {
                owner.Ended = true;
                var ended = owner.OnEnded;
                owner.OnEnded = null;
                ended?.Invoke();
            }
        }
    }
}
