// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Preview;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The three <c>aet/*</c> endpoints the preview panel talks to.
/// </summary>
public sealed class PreviewHandlersTest
{
    private static PreviewSceneBuilder Builder(StubAssets assets, params GameSymbol[] symbols)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };

        return new PreviewSceneBuilder(new FakeGameIndexService(index), new SchemaProviderBuilder().Build(),
            new FakeVariantTagSource(), assets, ResxMessageCatalog.Server("en"));
    }

    // ── the scene endpoint ────────────────────────────────────────────────────

    [Fact]
    public async Task GetPreviewScene_WithNeitherSubject_SaysSoRatherThanFailing()
    {
        var assets = new StubAssets();
        var handler =
            new GetPreviewSceneHandler(Builder(assets), assets, TestCatalogues.English, new NoWorkspaceIcons(),
                new PrioritisedModels());

        var result = await handler.Handle(new GetPreviewSceneParams(), CancellationToken.None);

        Assert.Contains(result.Scene.Problems, p => p.Severity == "error");
    }

    [Fact]
    public async Task GetPreviewScene_AcceptsAFileUriFromTheCustomEditor()
    {
        // The custom editor sends a file:/// URI; the XML sends a bare name. Both mean one model.
        var assets = new StubAssets().With("Data/Art/Models/Ev_stardestroyer.alo", [1, 2, 3]);
        var prioritised = new PrioritisedModels();
        var handler =
            new GetPreviewSceneHandler(Builder(assets), assets, TestCatalogues.English, new NoWorkspaceIcons(),
                prioritised);

        var result = await handler.Handle(
            new GetPreviewSceneParams
            {
                ModelReference = "file:///d:/mod/Data/Art/Models/Ev_stardestroyer.alo"
            },
            CancellationToken.None);

        var part = Assert.Single(result.Scene.Parts);
        Assert.Equal("Ev_stardestroyer.alo", part.ModelRef);
        Assert.True(part.Resolved);

        // Opening a subject puts its models at the front of the background build's queue (#436).
        var first = Assert.Single(Assert.Single(prioritised.Calls));
        Assert.Equal("Ev_stardestroyer.alo", first.ModelReference);
        Assert.Equal(result.Scene.Animations, first.Animations);
    }

    /// <summary>The background build as a record of what a scene asked it to do first.</summary>
    private sealed class PrioritisedModels : IPreviewCacheBuilder
    {
        public List<IReadOnlyList<PreviewBuildItem>> Calls { get; } = [];

        public void StartUnlessBuilt()
        {
        }

        public void Prioritise(IReadOnlyList<PreviewBuildItem> items)
        {
            Calls.Add(items);
        }

        public void FilesChanged(IReadOnlyCollection<string> paths)
        {
        }

        public void Cancel()
        {
        }
    }

    // ── the model list ────────────────────────────────────────────────────────

    private static ListModelsHandler ListModels(params string[] particleSystems)
    {
        var index = GameIndex.Empty with
        {
            AssetFiles = MergedAssetFileIndex.Merge(
                [
                    "data/art/models/ev_stardestroyer.alo", "data/art/models/p_smoke.alo",
                    "data/art/textures/ev_stardestroyer.tga", "data/art/models/ev_stardestroyer.ala"
                ],
                ["data/art/models/my_frigate.alo"]),
            ParticleSystems = particleSystems.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase)
        };
        return new ListModelsHandler(new FakeGameIndexService(index));
    }

    /// <summary>
    ///     What the "Preview Model" picker offers: every <c>.alo</c> the project can reach, its own
    ///     and the base game's, by file name - so a model in a dependency or a MEG can be opened
    ///     without knowing its name first.
    /// </summary>
    [Fact]
    public async Task ListModels_OffersEveryReachableAloByFileName()
    {
        var result = await ListModels().Handle(new ListModelsParams(), CancellationToken.None);

        Assert.Equal(
            ["ev_stardestroyer.alo", "my_frigate.alo", "p_smoke.alo"],
            result.Models.Select(m => m.Name));
    }

    // Models and particle systems share the extension (#270); the scan that reads every .alo knows
    // which is which. One it never classified - packed somewhere unscanned - is still offered.
    [Fact]
    public async Task ListModels_LeavesOutTheParticleSystemsTheCatalogKnows()
    {
        var result = await ListModels("P_SMOKE.ALO").Handle(new ListModelsParams(), CancellationToken.None);

        Assert.Equal(["ev_stardestroyer.alo", "my_frigate.alo"], result.Models.Select(m => m.Name));
    }

    [Fact]
    public async Task ListModels_SaysWhichModelsComeFromTheBaseGame()
    {
        var result = await ListModels().Handle(new ListModelsParams(), CancellationToken.None);

        Assert.True(result.Models.Single(m => m.Name == "ev_stardestroyer.alo").BaseGame);
        Assert.False(result.Models.Single(m => m.Name == "my_frigate.alo").BaseGame);
    }

    // ── the GLB endpoint ──────────────────────────────────────────────────────

    /// <summary>
    ///     The GLB handler over a preview cache with no project to keep anything in, so each test
    ///     converts for itself. <c>PreviewModelCacheTest</c> covers what is kept.
    /// </summary>
    private static GetModelGlbHandler GlbHandler(StubAssets assets, ILogger<GetModelGlbHandler>? logger = null,
        OfferedTransfers? transfers = null, IPreviewCacheLocation? location = null)
    {
        var disk = new FileHelper(new MockFileSystem());
        var cache = new PreviewModelCache(new PreviewAssetFingerprints(assets, disk), disk,
            location ?? new NoProject(), NullLogger<PreviewModelCache>.Instance);
        return new GetModelGlbHandler(assets, cache, new ModelGlbConverter(), transfers ?? new OfferedTransfers(),
            TestCatalogues.English,
            logger ?? NullLogger<GetModelGlbHandler>.Instance);
    }

    /// <summary>
    ///     A conversion takes seconds on a large model, and one under way on the background build's
    ///     thread is waited for. Neither holds the thread the request arrived on: the handler hands
    ///     back a task and the conversion runs elsewhere.
    /// </summary>
    [Fact]
    public async Task GetModelGlb_DoesNotHoldItsCallerWhileTheModelIsConverted()
    {
        using var cache = new HeldCache();
        var handler = new GetModelGlbHandler(new StubAssets(), cache, new ModelGlbConverter(),
            new OfferedTransfers(), TestCatalogues.English, NullLogger<GetModelGlbHandler>.Instance);

        var answer = handler.Handle(new GetModelGlbParams { ModelReference = "Hull.alo", Have = "v1" },
            CancellationToken.None);

        Assert.False(answer.IsCompleted);
        cache.Release();
        var result = await answer;
        Assert.True(cache.Released);
        Assert.Equal("v1", cache.Have);
        Assert.NotNull(result.Error);
    }

    /// <summary>A cache that holds every request until the test lets it go, and then finds nothing.</summary>
    private sealed class HeldCache : IPreviewModelCache, IDisposable
    {
        private readonly ManualResetEventSlim _release = new();

        /// <summary>Whether the request was let go by the test, rather than giving up its wait.</summary>
        public bool Released { get; private set; }

        public string? Have { get; private set; }

        public void Dispose()
        {
            _release.Dispose();
        }

        public PreviewCachedModel? GetOrConvert(string modelPath, IReadOnlyList<(string Name, string Path)> clips,
            PreviewModelConverter convert, PreviewClipJudge judge, string? have = null)
        {
            Have = have;
            Released = _release.Wait(TimeSpan.FromSeconds(5));
            return null;
        }

        public int Clear()
        {
            return 0;
        }

        public void Release()
        {
            _release.Set();
        }
    }

    /// <summary>
    ///     The transfer channel as a list of what was offered, with no pipe behind it. A payload is
    ///     opened when a test asks for its bytes, as the pipe opens it when the client asks.
    /// </summary>
    private sealed class OfferedTransfers : IPreviewTransferChannel
    {
        private readonly Dictionary<string, Func<Stream>> _payloads = [];

        /// <summary>What every offer fails with, for a channel that cannot be opened.</summary>
        public Exception? Fails { get; init; }

        public PreviewTransfer Offer(string format, long length, Func<Stream> open)
        {
            if (Fails is not null)
                throw Fails;

            var id = Guid.NewGuid().ToString("N");
            _payloads[id] = open;
            return new PreviewTransfer("test-pipe", id, length, format);
        }

        /// <summary>The payload as the pipe opens it; the caller closes it.</summary>
        public Stream Open(PreviewTransfer transfer)
        {
            return _payloads[transfer.Id]();
        }

        public byte[] BytesOf(PreviewTransfer transfer)
        {
            using var payload = _payloads[transfer.Id]();
            using var bytes = new MemoryStream();
            payload.CopyTo(bytes);
            return bytes.ToArray();
        }
    }

    private sealed class NoProject : IPreviewCacheLocation
    {
        public string? Directory => null;

        public void EnsureIgnored()
        {
        }
    }

    private sealed class ProjectAt(string directory) : IPreviewCacheLocation
    {
        public string? Directory => directory;

        public void EnsureIgnored()
        {
        }
    }

    /// <summary>
    ///     The second request is answered from the cache, and still says what the conversion said:
    ///     the notes are kept beside the entry, so a hit is not quieter than a miss.
    /// </summary>
    [Fact]
    public async Task GetModelGlb_AnsweredFromTheCache_SaysAgainWhatTheConversionSaid()
    {
        var model = FindModel("Ev_stardestroyer.alo");
        var animation = FindModel("Ai_rancor_idle_00.ala");
        if (model is null || animation is null)
            Assert.Skip("No extracted game tree found.");

        var assets = new StubAssets()
            .With("Data/Art/Models/Ev_stardestroyer.alo", File.ReadAllBytes(model))
            .With("Data/Art/Models/Ai_rancor_idle_00.ala", File.ReadAllBytes(animation));
        var logger = new ListLogger<GetModelGlbHandler>();
        var transfers = new OfferedTransfers();
        var handler = GlbHandler(assets, logger, transfers, new ProjectAt("/mod/.aetswg/preview"));
        var request = new GetModelGlbParams
        {
            ModelReference = "Ev_stardestroyer.alo",
            Animations = ["Ai_rancor_idle_00.ala"]
        };

        var first = await handler.Handle(request, CancellationToken.None);
        var second = await handler.Handle(request, CancellationToken.None);

        Assert.Equal(2, logger.Messages.Count(m => m.Contains("Ai_rancor_idle_00.ala")));
        Assert.Equal(logger.Messages[0], logger.Messages[1]);
        Assert.Equal(transfers.BytesOf(first.Transfer!), transfers.BytesOf(second.Transfer!));
        Assert.Equal(first.Animations, second.Animations);
    }

    [Fact]
    public async Task GetModelGlb_ForAMissingModel_ExplainsWhyRatherThanReturningNothing()
    {
        var assets = new StubAssets();
        var handler = GlbHandler(assets);

        var result = await handler.Handle(
            new GetModelGlbParams { ModelReference = "nope.alo" }, CancellationToken.None);

        Assert.Null(result.Transfer);
        Assert.Contains("baseGameDirectory", result.Error);
    }

    [Fact]
    public async Task GetModelGlb_ForAFileThatIsNotAModel_ReportsTheFormatComplaint()
    {
        // The reader is strict by design; its message names the chunk that did not add up, which is
        // far more use than a bare failure.
        var assets = new StubAssets().With("Data/Art/Models/junk.alo", [9, 9, 9, 9, 9, 9, 9, 9]);
        var handler = GlbHandler(assets);

        var result = await handler.Handle(
            new GetModelGlbParams { ModelReference = "junk.alo" }, CancellationToken.None);

        Assert.Null(result.Transfer);
        Assert.Contains("could not be read", result.Error);
    }

    [Fact]
    public async Task GetModelGlb_ForARealModel_OffersTheGlbAsATransferAndSendsNoBytesInTheReply()
    {
        var model = FindModel("Ev_stardestroyer.alo");
        if (model is null)
            Assert.Skip("No extracted game tree found.");

        var assets = new StubAssets().With("Data/Art/Models/Ev_stardestroyer.alo", File.ReadAllBytes(model));
        var transfers = new OfferedTransfers();
        var handler = GlbHandler(assets, transfers: transfers);

        var result = await handler.Handle(
            new GetModelGlbParams { ModelReference = "Ev_stardestroyer.alo" }, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Transfer);
        Assert.Equal("glb", result.Transfer.Format);

        // "glTF" in ASCII is the GLB magic: what was offered is a real container, not a blob.
        var bytes = transfers.BytesOf(result.Transfer);
        Assert.Equal(bytes.Length, result.Transfer.Length);
        Assert.Equal([0x67, 0x6C, 0x54, 0x46], bytes[..4]);
    }

    /// <summary>
    ///     A clip whose bone NAMES disagree is kept - it binds by index.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This assertion used to be <c>Assert.Empty</c>. The user reported that the
    ///         identical-skeleton rule fires all over the BASE GAME, on units whose animations play
    ///         correctly in the engine, so refusing a name disagreement was costing real clips.
    ///     </para>
    ///     <para>
    ///         The pairing is deliberately permissive now, and this test says so out loud: a rancor
    ///         idle is plainly not a Star Destroyer's clip, and it is loaded anyway. What keeps that
    ///         from happening in practice is the CALLER - <c>AnimationsFor</c> only ever offers clips
    ///         whose filename is prefixed by the model's own stem.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task GetModelGlb_KeepsAnAnimationWhoseBoneNamesDisagree()
    {
        var model = FindModel("Ev_stardestroyer.alo");
        var animation = FindModel("Ai_rancor_idle_00.ala");
        if (model is null || animation is null)
            Assert.Skip("No extracted game tree found.");

        var assets = new StubAssets()
            .With("Data/Art/Models/Ev_stardestroyer.alo", File.ReadAllBytes(model))
            .With("Data/Art/Models/Ai_rancor_idle_00.ala", File.ReadAllBytes(animation));
        var handler = GlbHandler(assets);

        var result = await handler.Handle(
            new GetModelGlbParams
            {
                ModelReference = "Ev_stardestroyer.alo",
                Animations = ["Ai_rancor_idle_00.ala"]
            },
            CancellationToken.None);

        Assert.NotNull(result.Transfer);
        Assert.Single(result.Animations);
    }

    /// <summary>
    ///     A clip reaching past the end of the bone list is still refused.
    /// </summary>
    /// <remarks>
    ///     The one half of the old rule that is not a strictness setting: there is no node to drive
    ///     past the end of the array, so the track cannot be written whatever anyone decides about
    ///     names. `Nb_basepad.alo` carries three bones; the rancor drives far more.
    /// </remarks>
    [Fact]
    public async Task GetModelGlb_DropsAnAnimationReachingPastTheSkeleton()
    {
        var model = FindModel("Nb_basepad.alo");
        var animation = FindModel("Ai_rancor_idle_00.ala");
        if (model is null || animation is null)
            Assert.Skip("No extracted game tree found.");

        var assets = new StubAssets()
            .With("Data/Art/Models/Nb_basepad.alo", File.ReadAllBytes(model))
            .With("Data/Art/Models/Ai_rancor_idle_00.ala", File.ReadAllBytes(animation));
        var handler = GlbHandler(assets);

        var result = await handler.Handle(
            new GetModelGlbParams
            {
                ModelReference = "Nb_basepad.alo",
                Animations = ["Ai_rancor_idle_00.ala"]
            },
            CancellationToken.None);

        Assert.NotNull(result.Transfer);
        Assert.Empty(result.Animations);
    }

    /// <summary>
    ///     A skeleton disagreement is said out loud, whether the clip was kept or dropped.
    /// </summary>
    /// <remarks>
    ///     Silence leaves no way to tell a clip that was rejected from one that was never there, and
    ///     now that a disagreeing clip is KEPT there is a second thing worth saying: it will play,
    ///     and the bones it moves may not be the ones it was authored to move. Either way the reason
    ///     names the bone, so a reader can see whether to suspect the model or the animation.
    /// </remarks>
    [Fact]
    public async Task GetModelGlb_SaysWhenAnAnimationDisagreesWithTheSkeleton()
    {
        var model = FindModel("Ev_stardestroyer.alo");
        var animation = FindModel("Ai_rancor_idle_00.ala");
        if (model is null || animation is null)
            Assert.Skip("No extracted game tree found.");

        var assets = new StubAssets()
            .With("Data/Art/Models/Ev_stardestroyer.alo", File.ReadAllBytes(model))
            .With("Data/Art/Models/Ai_rancor_idle_00.ala", File.ReadAllBytes(animation));
        var logger = new ListLogger<GetModelGlbHandler>();
        var handler = GlbHandler(assets, logger);

        await handler.Handle(
            new GetModelGlbParams
            {
                ModelReference = "Ev_stardestroyer.alo",
                Animations = ["Ai_rancor_idle_00.ala"]
            },
            CancellationToken.None);

        var said = Assert.Single(logger.Messages, m => m.Contains("Ai_rancor_idle_00.ala"));
        Assert.Contains("Ev_stardestroyer.alo", said);
        Assert.Contains("bone", said);
    }

    /// <summary>
    ///     A clip that drives a bone the SKELETON does not have is still this model's clip.
    /// </summary>
    /// <remarks>
    ///     The engine assumes a bone at the origin of every mesh, so an animation may legitimately
    ///     name one at an index past the end of the skeleton. `Ub_turretmst50_idle00` is the case:
    ///     its last driven bone is `collision`, which the turret carries only as a MESH, so pairing
    ///     against the skeleton alone dropped the model's only idle - silently, since a dropped clip
    ///     looks exactly like a clip that was never there.
    ///     <para>
    ///         Two of the 2826 name-paired model/clip pairs in the shipped FOC tree need this, and
    ///         none are lost to it. Small, and still right: the alternative is refusing a clip for
    ///         being what the engine calls valid.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task GetModelGlb_KeepsAClipThatDrivesAMeshOriginBone()
    {
        var model = FindModel("Ub_turretmst50.alo");
        var animation = FindModel("Ub_turretmst50_idle00.ala");
        if (model is null || animation is null)
            Assert.Skip("No extracted game tree found.");

        var assets = new StubAssets()
            .With("Data/Art/Models/Ub_turretmst50.alo", File.ReadAllBytes(model))
            .With("Data/Art/Models/Ub_turretmst50_idle00.ala", File.ReadAllBytes(animation));
        var handler = GlbHandler(assets);

        var result = await handler.Handle(
            new GetModelGlbParams
            {
                ModelReference = "Ub_turretmst50.alo",
                Animations = ["Ub_turretmst50_idle00.ala"]
            },
            CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(["Ub_turretmst50_idle00"], result.Animations);
    }

    // ── the texture endpoint ──────────────────────────────────────────────────

    private static GetModelTextureHandler TextureHandler(StubAssets assets, OfferedTransfers transfers,
        MockFileSystem? disk = null, IPreviewAssetFingerprints? fingerprints = null)
    {
        var files = new FileHelper(disk ?? new MockFileSystem());
        return new GetModelTextureHandler(assets, fingerprints ?? new PreviewAssetFingerprints(assets, files),
            files, transfers, TestCatalogues.English, NullLogger<GetModelTextureHandler>.Instance);
    }

    /// <summary>
    ///     A loose texture is streamed from the modder's own file while the client collects it. The
    ///     file must stay theirs meanwhile: saved over, replaced or deleted by whatever tool wrote it.
    /// </summary>
    [Fact]
    public async Task GetModelTexture_WhileALooseFileIsCollected_ItCanStillBeWrittenAndDeleted()
    {
        var folder = Directory.CreateTempSubdirectory("aet-preview-texture-");
        try
        {
            var path = Path.Combine(folder.FullName, "Hull.dds");
            await File.WriteAllBytesAsync(path, [5, 6, 7], TestContext.Current.CancellationToken);
            var assets = new StubAssets().With("Data/Art/Textures/Hull.dds", [5, 6, 7], path);
            var transfers = new OfferedTransfers();
            var files = new FileHelper(new System.IO.Abstractions.FileSystem());
            var handler = new GetModelTextureHandler(assets, new PreviewAssetFingerprints(assets, files), files,
                transfers, TestCatalogues.English, NullLogger<GetModelTextureHandler>.Instance);

            var result = await handler.Handle(new GetModelTextureParams { Name = "Hull.dds" },
                CancellationToken.None);

            await using (transfers.Open(result.Transfer!))
            {
                await File.WriteAllBytesAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);
                File.Delete(path);
            }

            Assert.False(File.Exists(path));
        }
        finally
        {
            folder.Delete(true);
        }
    }

    /// <summary>A transfer channel that cannot be opened is a texture that did not load, said as one.</summary>
    [Fact]
    public async Task GetModelTexture_WhenTheTransferChannelCannotBeOpened_SaysTheTextureCouldNotBeRead()
    {
        var assets = new StubAssets().With("Data/Art/Textures/Hull.dds", [5, 6, 7]);
        var handler = TextureHandler(assets, new OfferedTransfers { Fails = new IOException("no pipe") });

        var result = await handler.Handle(new GetModelTextureParams { Name = "Hull.dds" }, CancellationToken.None);

        Assert.Null(result.Transfer);
        Assert.NotNull(result.Error);
    }

    /// <summary>
    ///     A refresh asks for every texture again and says which version it holds. For one that did
    ///     not change the reply offers nothing - no bytes are read, held or sent (#436, FR-4).
    /// </summary>
    [Fact]
    public async Task GetModelTexture_WhenTheClientHoldsThatVersion_OffersNothing()
    {
        var disk = new MockFileSystem();
        disk.AddFile("/mod/Data/Art/Textures/Hull.dds", new MockFileData([5, 6, 7]));
        var assets = new StubAssets { Disk = disk }
            .With("Data/Art/Textures/Hull.dds", [0], "/mod/Data/Art/Textures/Hull.dds");
        var handler = TextureHandler(assets, new OfferedTransfers(), disk);
        var first = await handler.Handle(new GetModelTextureParams { Name = "Hull.tga" }, CancellationToken.None);

        var again = await handler.Handle(
            new GetModelTextureParams { Name = "Hull.tga", Have = first.Version }, CancellationToken.None);

        Assert.NotNull(first.Version);
        Assert.NotNull(first.Transfer);
        Assert.Equal(first.Version, again.Version);
        Assert.Null(again.Transfer);
        Assert.Null(again.Error);
        Assert.Equal("dds", again.Format);
    }

    /// <summary>
    ///     Every 2048x2048 DXT5 texture has the same length, and a copy keeps the source's write
    ///     time: a texture copied over another can have the stamp of the one it replaced. The
    ///     watcher's report is what makes it another version, and so sends it again.
    /// </summary>
    [Fact]
    public async Task GetModelTexture_WrittenOverWithTheSameLengthAndWriteTime_IsSentAgainOnceTheWatcherReportsIt()
    {
        var disk = new MockFileSystem();
        var written = new DateTime(2026, 5, 25, 13, 33, 0, DateTimeKind.Utc);
        disk.AddFile("/mod/Data/Art/Textures/Hull.dds", new MockFileData([5, 6, 7]) { LastWriteTime = written });
        var assets = new StubAssets { Disk = disk }
            .With("Data/Art/Textures/Hull.dds", [0], "/mod/Data/Art/Textures/Hull.dds");
        var transfers = new OfferedTransfers();
        var fingerprints = new PreviewAssetFingerprints(assets, new FileHelper(disk));
        var handler = TextureHandler(assets, transfers, disk, fingerprints);
        var first = await handler.Handle(new GetModelTextureParams { Name = "Hull.tga" }, CancellationToken.None);

        disk.File.WriteAllBytes("/mod/Data/Art/Textures/Hull.dds", [9, 9, 9]);
        disk.File.SetLastWriteTimeUtc("/mod/Data/Art/Textures/Hull.dds", written);
        var unreported = await handler.Handle(
            new GetModelTextureParams { Name = "Hull.tga", Have = first.Version }, CancellationToken.None);

        fingerprints.Forget([@"\mod\Data\Art\Textures\Hull.dds"]);
        var reported = await handler.Handle(
            new GetModelTextureParams { Name = "Hull.tga", Have = first.Version }, CancellationToken.None);

        // The stamp alone cannot tell: until the watcher speaks, the client's copy is taken to stand.
        Assert.Null(unreported.Transfer);
        Assert.NotEqual(first.Version, reported.Version);
        Assert.Equal([9, 9, 9], transfers.BytesOf(reported.Transfer!));

        // One write is often reported two or three times over. The second report finds the same
        // bytes, so the version the client now holds still stands and nothing is sent again.
        fingerprints.Forget([@"\mod\Data\Art\Textures\Hull.dds"]);
        var reportedAgain = await handler.Handle(
            new GetModelTextureParams { Name = "Hull.tga", Have = reported.Version }, CancellationToken.None);

        Assert.Null(reportedAgain.Transfer);
        Assert.Equal(reported.Version, reportedAgain.Version);
    }

    /// <summary>An archive entry has no stamp; its version is its bytes' hash.</summary>
    [Fact]
    public async Task GetModelTexture_ForAnEntryWithNoFile_VersionsItByItsBytes()
    {
        var assets = new StubAssets()
            .With("Data/Art/Textures/Packed.dds", [1, 2], "Data/Art/Textures/Packed.dds");
        var handler = TextureHandler(assets, new OfferedTransfers());
        var first = await handler.Handle(new GetModelTextureParams { Name = "Packed.dds" }, CancellationToken.None);

        var same = await handler.Handle(
            new GetModelTextureParams { Name = "Packed.dds", Have = first.Version }, CancellationToken.None);
        assets.With("Data/Art/Textures/Packed.dds", [3, 4], "Data/Art/Textures/Packed.dds");
        var changed = await handler.Handle(
            new GetModelTextureParams { Name = "Packed.dds", Have = first.Version }, CancellationToken.None);

        Assert.Null(same.Transfer);
        Assert.NotNull(changed.Transfer);
        Assert.NotEqual(first.Version, changed.Version);
    }

    /// <summary>
    ///     The model's version is its cache key. A client that holds it is offered nothing, and is
    ///     still told which clips are baked in.
    /// </summary>
    [Fact]
    public async Task GetModelGlb_WhenTheClientHoldsThatVersion_OffersNothing()
    {
        var model = FindModel("Ev_stardestroyer.alo");
        if (model is null)
            Assert.Skip("No extracted game tree found.");

        var assets = new StubAssets().With("Data/Art/Models/Ev_stardestroyer.alo", File.ReadAllBytes(model));
        var handler = GlbHandler(assets, location: new ProjectAt("/mod/.aetswg/preview"));
        var first = await handler.Handle(
            new GetModelGlbParams { ModelReference = "Ev_stardestroyer.alo" }, CancellationToken.None);

        var again = await handler.Handle(
            new GetModelGlbParams { ModelReference = "Ev_stardestroyer.alo", Have = first.Version },
            CancellationToken.None);
        var other = await handler.Handle(
            new GetModelGlbParams { ModelReference = "Ev_stardestroyer.alo", Have = "another-version" },
            CancellationToken.None);

        Assert.NotNull(first.Version);
        Assert.NotNull(first.Transfer);
        Assert.Null(again.Transfer);
        Assert.Null(again.Error);
        Assert.Equal(first.Version, again.Version);
        Assert.NotNull(other.Transfer);
    }

    /// <summary>
    ///     A hardpoint's model is asked for with the UNIT's clips, and the background build
    ///     converts it with its own (#436). A clip that reaches past the model's bones is not baked,
    ///     so both mean the same GLB: the same version, and the request still says why the clip is
    ///     not there. On the shipped files - a turret of 15 bone targets, and a creature's idle.
    /// </summary>
    [Fact]
    public async Task GetModelGlb_ForAPartOfferedAClipThatCannotPlayOnIt_IsTheVersionConvertedWithoutIt()
    {
        var model = FindModel("Ub_turretmst50.alo");
        var animation = FindModel("Ai_rancor_idle_00.ala");
        if (model is null || animation is null)
            Assert.Skip("No extracted game tree found.");

        var assets = new StubAssets()
            .With("Data/Art/Models/Ub_turretmst50.alo", File.ReadAllBytes(model))
            .With("Data/Art/Models/Ai_rancor_idle_00.ala", File.ReadAllBytes(animation));
        var logger = new ListLogger<GetModelGlbHandler>();
        var transfers = new OfferedTransfers();
        var handler = GlbHandler(assets, logger, transfers, new ProjectAt("/mod/.aetswg/preview"));

        var alone = await handler.Handle(
            new GetModelGlbParams { ModelReference = "Ub_turretmst50.alo" }, CancellationToken.None);
        var asPart = await handler.Handle(
            new GetModelGlbParams
            {
                ModelReference = "Ub_turretmst50.alo",
                Animations = ["Ai_rancor_idle_00.ala"]
            },
            CancellationToken.None);

        Assert.Empty(asPart.Animations);
        Assert.Equal(alone.Version, asPart.Version);
        Assert.Equal(transfers.BytesOf(alone.Transfer!), transfers.BytesOf(asPart.Transfer!));
        Assert.Contains(logger.Messages,
            m => m.Contains("Ai_rancor_idle_00.ala") && m.Contains("cannot be played"));
    }

    [Fact]
    public async Task GetModelTexture_ReportsTheFormatActuallyFoundNotTheOneAsked()
    {
        // Most shipped models reference a .tga name for a file that only ever ships as .dds. The
        // client picks its decoder from the reported format, so reporting the request would be wrong.
        var assets = new StubAssets()
            .With("Data/Art/Textures/Ai_rancor.dds", [1, 2, 3, 4], "Data/Art/Textures/Ai_rancor.dds");
        var transfers = new OfferedTransfers();
        var handler = TextureHandler(assets, transfers);

        var result = await handler.Handle(
            new GetModelTextureParams { Name = "Ai_rancor.tga" }, CancellationToken.None);

        Assert.Equal("dds", result.Format);
        Assert.NotNull(result.Transfer);
        Assert.Equal("dds", result.Transfer.Format);
        Assert.Equal(4, result.Transfer.Length);
        Assert.Equal([1, 2, 3, 4], transfers.BytesOf(result.Transfer));
    }

    /// <summary>
    ///     A loose file is read once, for its version, and never again while its stamp stands; it is
    ///     offered by its length and streamed from disk when the client collects it. MEASURED
    ///     before: every texture was read whole and base64-encoded inside every request - 16 ms for
    ///     a 16.8 MB sprite map; the read and hash take 5 ms, once.
    /// </summary>
    [Fact]
    public async Task GetModelTexture_ForALooseFile_ReadsItOnceForItsVersionAndStreamsItFromDisk()
    {
        var disk = new MockFileSystem();
        disk.AddFile("/mod/Data/Art/Textures/Hull.dds", new MockFileData([5, 6, 7]));
        var assets = new StubAssets { Disk = disk }
            .With("Data/Art/Textures/Hull.dds", [0], "/mod/Data/Art/Textures/Hull.dds");
        var transfers = new OfferedTransfers();
        var handler = TextureHandler(assets, transfers, disk);

        var result = await handler.Handle(
            new GetModelTextureParams { Name = "Hull.tga" }, CancellationToken.None);
        await handler.Handle(new GetModelTextureParams { Name = "Hull.tga" }, CancellationToken.None);

        Assert.Equal(1, assets.Reads);
        Assert.Equal(3, result.Transfer!.Length);
        // What is collected is what the disk holds then, not what a resolver read earlier.
        Assert.Equal([5, 6, 7], transfers.BytesOf(result.Transfer));
    }

    /// <summary>The name is resolved once: the location found is the location read.</summary>
    [Fact]
    public async Task GetModelTexture_ResolvesTheNameOnce()
    {
        var assets = new StubAssets()
            .With("Data/Art/Textures/Packed.dds", [1, 2], "Data/Art/Textures/Packed.dds");
        var handler = TextureHandler(assets, new OfferedTransfers());

        await handler.Handle(new GetModelTextureParams { Name = "Packed.dds" }, CancellationToken.None);

        Assert.Equal(1, assets.Locates);
        Assert.Equal(1, assets.Reads);
    }

    [Fact]
    public async Task GetModelTexture_ForAMissingTexture_ExplainsWhy()
    {
        var handler = TextureHandler(new StubAssets(), new OfferedTransfers());

        var result = await handler.Handle(
            new GetModelTextureParams { Name = "nope.tga" }, CancellationToken.None);

        Assert.Null(result.Transfer);
        Assert.Contains("not found", result.Error);
    }

    // ── reference normalisation ───────────────────────────────────────────────

    [Theory]
    [InlineData("EV_StarDestroyer.ALO", "EV_StarDestroyer.ALO")]
    [InlineData("file:///d:/mod/Data/Art/Models/hull.alo", "hull.alo")]
    [InlineData(@"Data\Art\Models\hull.alo", "hull.alo")]
    [InlineData("  hull.alo  ", "hull.alo")]
    [InlineData(null, "")]
    public void Normalise_ReducesEveryCallersShapeToABareModelName(string? input, string expected)
    {
        Assert.Equal(expected, PreviewModelReference.Normalise(input));
    }

    /// <summary>
    ///     One shipped art file, from either test tree.
    /// </summary>
    /// <remarks>
    ///     Both trees, because they do not hold the same models: everything Underworld - the turrets
    ///     this file pairs animations against among them - ships only with Forces of Corruption.
    /// </remarks>
    private static string? FindModel(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var tree in new[] { "eaw", "foc" })
            {
                var candidate = Path.Combine(dir.FullName, tree, "Data", "Art", "Models", name);
                if (File.Exists(candidate))
                    return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>Serves the exact game-relative paths it was given.</summary>
    // ── the particle endpoint ──────────────────────────────────────────────
    [Fact]
    public async Task GetParticleSystem_ResolvesAGameObjectToItsModel()
    {
        // `Death_Explosions` and `Debris_Attached_Particle` name a <Particle> OBJECT, not a file.
        // Reading the name as a file is why no death explosion has ever played: the request went out
        // for `Large_Explosion_Space_Empire.alo`, which does not exist anywhere.
        var tags = new FakeVariantTagSource().With("Large_Explosion_Space_Empire",
            Tag("Space_Model_Name", "p_explosion_empire_big00.alo"));

        var result = await Particles(new StubAssets(), tags,
            Sym("Large_Explosion_Space_Empire", "GameObjectType")).Handle(
            new GetParticleSystemParams { Name = "Large_Explosion_Space_Empire" },
            CancellationToken.None);

        Assert.Contains("p_explosion_empire_big00.alo", result.Error);
    }

    [Fact]
    public async Task GetParticleSystem_FallsBackToTheLandModel()
    {
        // 12 of foc's particle objects declare only the land model. Nothing about a particle is
        // theatre-specific, so a space request takes it rather than reporting nothing.
        var tags = new FakeVariantTagSource().With("Ground_Puff",
            Tag("Land_Model_Name", "p_smoke_small.alo"));

        var result = await Particles(new StubAssets(), tags, Sym("Ground_Puff", "GameObjectType")).Handle(
            new GetParticleSystemParams { Name = "Ground_Puff" }, CancellationToken.None);

        Assert.Contains("p_smoke_small.alo", result.Error);
    }

    [Fact]
    public async Task GetParticleSystem_StillTakesABareFileName()
    {
        // The proxies name the asset directly, and that is most of the traffic. Resolving objects
        // must not cost them anything.
        var result = await Particles(new StubAssets(), new FakeVariantTagSource()).Handle(
            new GetParticleSystemParams { Name = "pe_stardestroyerengines" }, CancellationToken.None);

        Assert.Contains("pe_stardestroyerengines.alo", result.Error);
    }

    [Theory]
    [InlineData("p_smoke_small_thin_ALT2", "p_smoke_small_thin.alo")]
    [InlineData("p_electricalstatic_ALT1", "p_electricalstatic.alo")]
    [InlineData("p_blink_white_ALT0", "p_blink_white.alo")]
    [InlineData("p_smoke_tiny01_LOD1", "p_smoke_tiny01.alo")]
    [InlineData("p_fire_small01_ALT3_LOD0", "p_fire_small01.alo")]
    [InlineData("p_fire_small01_LOD0_ALT3", "p_fire_small01.alo")]
    [InlineData("p_smoke_small_thin_ALT2.alo", "p_smoke_small_thin.alo")]
    public async Task GetParticleSystem_StripsTheLevelSuffixTheEngineStrips(
        string proxyName, string expected)
    {
        // A proxy carries its damage stage in its NAME, and the asset is filed under the bare name:
        // `p_smoke_small_thin_ALT2` is not a file, `p_smoke_small_thin.alo` is. The engine strips the
        // suffix at the load site - `ObjectTemplate.cpp` walks `_ALT` and `_LOD` out of the name
        // before `Assets::LoadParticleSystem` - so every level-tagged effect in the corpus was asking
        // for a file that does not exist. 152 shipped models carry 206 such names between them.
        var result = await Particles(new StubAssets(), new FakeVariantTagSource()).Handle(
            new GetParticleSystemParams { Name = proxyName }, CancellationToken.None);

        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public async Task GetParticleSystem_NamesTheFileItRead_AndSaysASkippedPropertyOnce()
    {
        // #394: the dock's two damage stages ask for p_rb_station_damage_ALT1 and _ALT2, which are one
        // file. The answer names the file it read, so the panel can say that file's notes once; and the
        // property every emitter carries is one note, not one per emitter.
        var bytes = ParticleFileBytes.ParticleSystem(
            ParticleFileBytes.Emitter(null, ParticleFileBytes.Mini(0x06, ParticleFileBytes.I32(1))),
            ParticleFileBytes.Emitter(null, ParticleFileBytes.Mini(0x06, ParticleFileBytes.I32(1))),
            ParticleFileBytes.Emitter(null, ParticleFileBytes.Mini(0x06, ParticleFileBytes.I32(1))));
        var assets = new StubAssets().With("Data/Art/Models/p_rb_station_damage.alo", bytes);

        var result = await Particles(assets, new FakeVariantTagSource()).Handle(
            new GetParticleSystemParams { Name = "p_rb_station_damage_ALT2" }, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("p_rb_station_damage.alo", result.File);
        var problem = Assert.Single(result.Problems);
        Assert.Equal("Emitters 0-2 - property 0x06 is read past by the game and has no effect", problem.Message);
    }

    [Theory]
    [InlineData("p_alterac")]
    [InlineData("p_lodestone")]
    [InlineData("p_smoke_ALT")]
    [InlineData("p_smoke_LODGE")]
    public async Task GetParticleSystem_LeavesANameThatOnlyLooksLevelTagged(string proxyName)
    {
        // The marker counts only where DIGITS follow it, which is the same rule `ParseName` uses to
        // read the level - a suffix with nothing numeric after it is part of the name.
        var result = await Particles(new StubAssets(), new FakeVariantTagSource()).Handle(
            new GetParticleSystemParams { Name = proxyName }, CancellationToken.None);

        Assert.Contains($"{proxyName}.alo", result.Error);
    }

    [Fact]
    public async Task GetParticleSystem_CarriesTheObjectsScaleFactor()
    {
        // `Scale_Factor` sits on the base GameObjectType beside Mass and LOD_Bias
        // (`DatabaseMapExport.xml`), so it is a general object RENDER SCALE rather than anything
        // particle-specific - and nothing read it at all. Six shipped particle objects declare one:
        // 20.0 on the four hero powerup effects, 2.0 on the two bombing-run explosions, so those six
        // drew at a twentieth and a half of their intended size.
        var tags = new FakeVariantTagSource().With("Empire_Bombing_Run_Explosion_Land",
            Tag("Space_Model_Name", "p_explosion_thermal_empire.alo"),
            Tag("Scale_Factor", "2.0"));

        var result = await Particles(new StubAssets(), tags,
            Sym("Empire_Bombing_Run_Explosion_Land", "GameObjectType")).Handle(
            new GetParticleSystemParams { Name = "Empire_Bombing_Run_Explosion_Land" },
            CancellationToken.None);

        Assert.Equal(2f, result.ScaleFactor);
    }

    [Fact]
    public async Task GetParticleSystem_ScaleFactorIsOneWhenTheObjectDeclaresNone()
    {
        // 91 of the shipped particle objects write 1.0 explicitly and the rest write nothing.
        var tags = new FakeVariantTagSource().With("Plain",
            Tag("Space_Model_Name", "p_plain.alo"));

        var result = await Particles(new StubAssets(), tags, Sym("Plain", "GameObjectType")).Handle(
            new GetParticleSystemParams { Name = "Plain" }, CancellationToken.None);

        Assert.Equal(1f, result.ScaleFactor);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("not a number")]
    public async Task GetParticleSystem_RefusesAScaleFactorThatWouldMakeTheSystemVanish(string raw)
    {
        // The reference guards the same way (`GameObjectCatalog.cpp`: reject non-finite and
        // non-positive) - a zero or negative scale collapses the object instead of sizing it.
        var tags = new FakeVariantTagSource().With("Odd",
            Tag("Space_Model_Name", "p_odd.alo"), Tag("Scale_Factor", raw));

        var result = await Particles(new StubAssets(), tags, Sym("Odd", "GameObjectType")).Handle(
            new GetParticleSystemParams { Name = "Odd" }, CancellationToken.None);

        Assert.Equal(1f, result.ScaleFactor);
    }

    [Fact]
    public async Task GetParticleSystem_ScaleFactorIsOneForABareAssetName()
    {
        // A model's proxy names the asset, and there is no object to carry a scale.
        var result = await Particles(new StubAssets(), new FakeVariantTagSource()).Handle(
            new GetParticleSystemParams { Name = "pe_stardestroyerengines" }, CancellationToken.None);

        Assert.Equal(1f, result.ScaleFactor);
    }

    // The catalogue offers every projectile in the tree - 105 of them, measured - but filling the
    // weapon from one only ever worked for the handful the SUBJECT fires, because those are the only
    // ones the scene resolves. Everything else printed "not loaded yet", which is why the picker read
    // as broken and the list as incomplete. This is the round trip that fills the rest.
    [Fact]
    public async Task GetProjectile_ResolvesOneByIdWithItsDamageValues()
    {
        var tags = new FakeVariantTagSource().With("Proj_Ion_Cannon",
            Tag("Projectile_Damage", "250"),
            Tag("Damage_Type", "Damage_Ion"),
            Tag("Projectile_Does_Shield_Damage", "Yes"),
            Tag("Projectile_Does_Hitpoint_Damage", "No"));

        var result = await Projectiles(tags, Sym("Proj_Ion_Cannon", "GameObjectType")).Handle(
            new GetProjectileParams { Name = "Proj_Ion_Cannon" }, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Projectile);
        Assert.Equal("Proj_Ion_Cannon", result.Projectile!.Id);
        Assert.Equal(250, result.Projectile.Damage);
        Assert.Equal("Damage_Ion", result.Projectile.DamageType);
        Assert.True(result.Projectile.DoesShieldDamage);
        Assert.False(result.Projectile.DoesHitpointDamage);
    }

    [Fact]
    public async Task GetProjectile_SaysSoForAnIdTheProjectDoesNotDefine()
    {
        // Better than an empty weapon: a name the tree does not carry is the reader's own typo or a
        // mod that moved, and silently filling zeroes would look like the projectile does nothing.
        var result = await Projectiles(new FakeVariantTagSource()).Handle(
            new GetProjectileParams { Name = "Proj_Nowhere" }, CancellationToken.None);

        Assert.Null(result.Projectile);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task GetProjectile_RefusesAnEmptyName()
    {
        var result = await Projectiles(new FakeVariantTagSource()).Handle(
            new GetProjectileParams { Name = "  " }, CancellationToken.None);

        Assert.Null(result.Projectile);
        Assert.NotNull(result.Error);
    }

    private static GetProjectileHandler Projectiles(
        FakeVariantTagSource tags, params GameSymbol[] symbols)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };

        return new GetProjectileHandler(
            TestCatalogues.English, new FakeGameIndexService(index), new SchemaProviderBuilder().Build(), tags);
    }

    private static GetParticleSystemHandler Particles(
        StubAssets assets, FakeVariantTagSource tags, params GameSymbol[] symbols)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };

        return new GetParticleSystemHandler(
            assets, TestCatalogues.English, new FakeGameIndexService(index), new SchemaProviderBuilder().Build(), tags);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    private static GameSymbol Sym(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin($"file:///{id}.xml", 0, 0), null);
    }

    /// <summary>Keeps what was logged, so a test can assert the preview said what it did.</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages => _messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }
    }

    private sealed class StubAssets : IGameAssetResolver
    {
        private readonly Dictionary<string, (byte[] Bytes, string Resolved)> _files =
            new(StringComparer.OrdinalIgnoreCase);

        public GameAssetTiers Tiers => new(0, false, false, 0);

        public GameAssetLocation? Locate(string gameRelativePath)
        {
            Locates++;
            return _files.TryGetValue(gameRelativePath, out var file)
                ? new GameAssetLocation(gameRelativePath, file.Resolved, GameAssetTier.Workspace)
                : null;
        }

        public byte[]? Read(string gameRelativePath)
        {
            Reads++;
            return _files.TryGetValue(gameRelativePath, out var file) ? file.Bytes : null;
        }

        /// <summary>The disk a located file is read from, when a test puts its files on one.</summary>
        public MockFileSystem? Disk { get; init; }

        public byte[]? Read(GameAssetLocation location)
        {
            if (Disk is null || !Disk.File.Exists(location.ResolvedPath))
                return Read(location.RequestedPath);

            Reads++;
            return Disk.File.ReadAllBytes(location.ResolvedPath);
        }

        /// <summary>How often a path was resolved, and how often bytes were read.</summary>
        public int Locates { get; private set; }

        public int Reads { get; private set; }

        /// <param name="path">The asset's game-relative path.</param>
        /// <param name="bytes">The asset's bytes.</param>
        /// <param name="resolvedAs">
        ///     The path to report as found, which may differ in extension from the request - that is
        ///     the whole point of the .tga/.dds interchange.
        /// </param>
        public StubAssets With(string path, byte[] bytes, string? resolvedAs = null)
        {
            _files[path] = (bytes, resolvedAs ?? path);

            // The interchange happens in the real resolver, so the stub mirrors it: a .dds on disk
            // answers a .tga request.
            var extension = Path.GetExtension(path);
            if (extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
                _files[Path.ChangeExtension(path, ".tga")] = (bytes, resolvedAs ?? path);

            return this;
        }
    }
}
