// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The three <c>aet/*</c> endpoints the preview panel talks to.
/// </summary>
public sealed class PreviewHandlersTest
{
    private static readonly string[] NoAnimations = [];

    private static ILspConfigurationProvider Config(bool modelPreview = true)
    {
        return new StubConfig(new LspConfiguration
        {
            Features = new FeatureFlags { Tools = new ToolsFeatureFlags { ModelPreview = modelPreview } }
        });
    }

    private static PreviewSceneBuilder Builder(StubAssets assets, params GameSymbol[] symbols)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };

        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            new FakeVariantTagSource(), assets);
    }

    // ── the scene endpoint ────────────────────────────────────────────────────

    [Fact]
    public async Task GetPreviewScene_WithNeitherSubject_SaysSoRatherThanFailing()
    {
        var assets = new StubAssets();
        var handler = new GetPreviewSceneHandler(Builder(assets), assets, Config());

        var result = await handler.Handle(new GetPreviewSceneParams(), CancellationToken.None);

        Assert.Contains(result.Scene.Problems, p => p.Severity == "error");
    }

    [Fact]
    public async Task GetPreviewScene_WhenTheFeatureIsOff_ExplainsItselfInsteadOfErroring()
    {
        // A panel that says why it is empty beats a failed request the user cannot interpret.
        var assets = new StubAssets();
        var handler = new GetPreviewSceneHandler(Builder(assets), assets, Config(modelPreview: false));

        var result = await handler.Handle(
            new GetPreviewSceneParams { ModelReference = "x.alo" }, CancellationToken.None);

        Assert.Contains(result.Scene.Problems, p => p.Message.Contains("modelPreview"));
    }

    [Fact]
    public async Task GetPreviewScene_AcceptsAFileUriFromTheCustomEditor()
    {
        // The custom editor sends a file:/// URI; the XML sends a bare name. Both mean one model.
        var assets = new StubAssets().With("Data/Art/Models/Ev_stardestroyer.alo", [1, 2, 3]);
        var handler = new GetPreviewSceneHandler(Builder(assets), assets, Config());

        var result = await handler.Handle(
            new GetPreviewSceneParams
            {
                ModelReference = "file:///d:/mod/Data/Art/Models/Ev_stardestroyer.alo"
            },
            CancellationToken.None);

        var part = Assert.Single(result.Scene.Parts);
        Assert.Equal("Ev_stardestroyer.alo", part.ModelRef);
        Assert.True(part.Resolved);
    }

    // ── the GLB endpoint ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetModelGlb_ForAMissingModel_ExplainsWhyRatherThanReturningNothing()
    {
        var assets = new StubAssets();
        var handler = new GetModelGlbHandler(assets, Config(), NullLogger<GetModelGlbHandler>.Instance);

        var result = await handler.Handle(
            new GetModelGlbParams { ModelReference = "nope.alo" }, CancellationToken.None);

        Assert.Null(result.Glb);
        Assert.Contains("baseGameDirectory", result.Error);
    }

    [Fact]
    public async Task GetModelGlb_ForAFileThatIsNotAModel_ReportsTheFormatComplaint()
    {
        // The reader is strict by design; its message names the chunk that did not add up, which is
        // far more use than a bare failure.
        var assets = new StubAssets().With("Data/Art/Models/junk.alo", [9, 9, 9, 9, 9, 9, 9, 9]);
        var handler = new GetModelGlbHandler(assets, Config(), NullLogger<GetModelGlbHandler>.Instance);

        var result = await handler.Handle(
            new GetModelGlbParams { ModelReference = "junk.alo" }, CancellationToken.None);

        Assert.Null(result.Glb);
        Assert.Contains("could not be read", result.Error);
    }

    [Fact]
    public async Task GetModelGlb_ForARealModel_ReturnsBase64Glb()
    {
        var model = FindModel("Ev_stardestroyer.alo");
        if (model is null)
            Assert.Skip("No extracted game tree found.");

        var assets = new StubAssets().With("Data/Art/Models/Ev_stardestroyer.alo", File.ReadAllBytes(model));
        var handler = new GetModelGlbHandler(assets, Config(), NullLogger<GetModelGlbHandler>.Instance);

        var result = await handler.Handle(
            new GetModelGlbParams { ModelReference = "Ev_stardestroyer.alo" }, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Glb);

        // "glTF" in ASCII is the GLB magic; decoding proves this is a real container, not a blob.
        var bytes = Convert.FromBase64String(result.Glb);
        Assert.Equal<byte[]>([0x67, 0x6C, 0x54, 0x46], bytes[..4]);
    }

    [Fact]
    public async Task GetModelGlb_DropsAnAnimationThatDoesNotMatchTheSkeleton()
    {
        // Ordinary data, not an error: shipped animations sit beside models they do not belong to.
        var model = FindModel("Ev_stardestroyer.alo");
        var animation = FindModel("Ai_rancor_idle_00.ala");
        if (model is null || animation is null)
            Assert.Skip("No extracted game tree found.");

        var assets = new StubAssets()
            .With("Data/Art/Models/Ev_stardestroyer.alo", File.ReadAllBytes(model))
            .With("Data/Art/Models/Ai_rancor_idle_00.ala", File.ReadAllBytes(animation));
        var handler = new GetModelGlbHandler(assets, Config(), NullLogger<GetModelGlbHandler>.Instance);

        var result = await handler.Handle(
            new GetModelGlbParams
            {
                ModelReference = "Ev_stardestroyer.alo",
                Animations = ["Ai_rancor_idle_00.ala"]
            },
            CancellationToken.None);

        Assert.NotNull(result.Glb);
        Assert.Empty(result.Animations);
    }

    /// <summary>
    ///     A dropped clip says which one it was and what did not line up.
    /// </summary>
    /// <remarks>
    ///     Dropping is right - shipped animations sit beside models they were not authored against -
    ///     but doing it in silence leaves no way to tell a clip that was rejected from one that was
    ///     never there. The reason names the bone, so a reader can see at once whether to suspect
    ///     the model or the animation.
    /// </remarks>
    [Fact]
    public async Task GetModelGlb_SaysWhyItDroppedAnAnimation()
    {
        var model = FindModel("Ev_stardestroyer.alo");
        var animation = FindModel("Ai_rancor_idle_00.ala");
        if (model is null || animation is null)
            Assert.Skip("No extracted game tree found.");

        var assets = new StubAssets()
            .With("Data/Art/Models/Ev_stardestroyer.alo", File.ReadAllBytes(model))
            .With("Data/Art/Models/Ai_rancor_idle_00.ala", File.ReadAllBytes(animation));
        var logger = new ListLogger<GetModelGlbHandler>();
        var handler = new GetModelGlbHandler(assets, Config(), logger);

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
        var handler = new GetModelGlbHandler(assets, Config(), NullLogger<GetModelGlbHandler>.Instance);

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

    [Fact]
    public async Task GetModelTexture_ReportsTheFormatActuallyFoundNotTheOneAsked()
    {
        // Most shipped models reference a .tga name for a file that only ever ships as .dds. The
        // client picks its decoder from the reported format, so reporting the request would be wrong.
        var assets = new StubAssets()
            .With("Data/Art/Textures/Ai_rancor.dds", [1, 2, 3, 4], "Data/Art/Textures/Ai_rancor.dds");
        var handler = new GetModelTextureHandler(assets, Config());

        var result = await handler.Handle(
            new GetModelTextureParams { Name = "Ai_rancor.tga" }, CancellationToken.None);

        Assert.Equal("dds", result.Format);
        Assert.Equal(Convert.ToBase64String([1, 2, 3, 4]), result.Data);
    }

    [Fact]
    public async Task GetModelTexture_ForAMissingTexture_ExplainsWhy()
    {
        var handler = new GetModelTextureHandler(new StubAssets(), Config());

        var result = await handler.Handle(
            new GetModelTextureParams { Name = "nope.tga" }, CancellationToken.None);

        Assert.Null(result.Data);
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

    /// <summary>Keeps what was logged, so a test can assert the preview said what it did.</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly List<string> messages = [];

        public IReadOnlyList<string> Messages => messages;

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
            messages.Add(formatter(state, exception));
        }
    }

    private sealed class StubConfig(LspConfiguration current) : ILspConfigurationProvider
    {
        public LspConfiguration Current { get; } = current;

        public void LoadFrom(object? initializationOptions)
        {
        }
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
            Sym("Large_Explosion_Space_Empire", "Particle")).Handle(
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

        var result = await Particles(new StubAssets(), tags, Sym("Ground_Puff", "Particle")).Handle(
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
            Sym("Empire_Bombing_Run_Explosion_Land", "Particle")).Handle(
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

        var result = await Particles(new StubAssets(), tags, Sym("Plain", "Particle")).Handle(
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

        var result = await Particles(new StubAssets(), tags, Sym("Odd", "Particle")).Handle(
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

    private static GetParticleSystemHandler Particles(
        StubAssets assets, FakeVariantTagSource tags, params GameSymbol[] symbols)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };

        return new GetParticleSystemHandler(
            assets, Config(), new FakeGameIndexService(index), new NullSchemaProvider(), tags);
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

    private sealed class StubAssets : IGameAssetResolver
    {
        private readonly Dictionary<string, (byte[] Bytes, string Resolved)> _files =
            new(StringComparer.OrdinalIgnoreCase);

        public GameAssetTiers Tiers => new(0, false, false, 0);

        public GameAssetLocation? Locate(string gameRelativePath)
        {
            return _files.TryGetValue(gameRelativePath, out var file)
                ? new GameAssetLocation(gameRelativePath, file.Resolved, GameAssetTier.Workspace)
                : null;
        }

        public byte[]? Read(string gameRelativePath)
        {
            return _files.TryGetValue(gameRelativePath, out var file) ? file.Bytes : null;
        }

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
