// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Schema.Versioning;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Server.Status;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Server.Tests.Startup;

public sealed class BaselineBootstrapperTest
{
    private const string BaselineDir = "/baselines";

    // A local path naming the baseline repository's folder serves both games from it.
    [Theory]
    [InlineData(TargetGame.EAW, "EAW_ONLY")]
    [InlineData(TargetGame.FOC, "FOC_ONLY")]
    public async Task LoadAsync_LocalFolder_AppliesThatGamesBaseline(TargetGame game, string expected)
    {
        var (bootstrapper, index, _) = Build();

        await bootstrapper.LoadAsync(game, CancellationToken.None);

        Assert.Equal([expected], index.Current.Baseline.Symbols.Keys);
    }

    // The reload path asks again on every project load; the same game must not fetch again.
    [Fact]
    public async Task LoadAsync_SameGameTwice_AppliesOnce()
    {
        var (bootstrapper, _, applications) = Build();

        await bootstrapper.LoadAsync(TargetGame.EAW, CancellationToken.None);
        await bootstrapper.LoadAsync(TargetGame.EAW, CancellationToken.None);

        Assert.Equal(1, applications());
    }

    // A project file edited to name the other game swaps the baseline under it.
    [Fact]
    public async Task LoadAsync_OtherGame_Switches()
    {
        var (bootstrapper, index, applications) = Build();

        await bootstrapper.LoadAsync(TargetGame.EAW, CancellationToken.None);
        await bootstrapper.LoadAsync(TargetGame.FOC, CancellationToken.None);

        Assert.Equal(2, applications());
        Assert.Equal(["FOC_ONLY"], index.Current.Baseline.Symbols.Keys);
        Assert.Equal(TargetGame.FOC, bootstrapper.LoadedGame);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (BaselineBootstrapper Bootstrapper, FakeGameIndexService Index, Func<int> Applications)
        Build()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$"{BaselineDir}/eaw/aet-pg-swg-lsp-eaw-baseline.aet"] = new(Baseline("EAW_ONLY")),
            [$"{BaselineDir}/foc/aet-pg-swg-lsp-foc-baseline.aet"] = new(Baseline("FOC_ONLY"))
        });

        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportPetroglyphHashing();
        services.SupportLocalisationBaseline();
        var sp = services.BuildServiceProvider();

        var client = new HttpClient(new FakeHttpHandler(_ => throw new InvalidOperationException("no network")));
        var loader = new BaselineLoader(client,
            new BaselineLocationResolver(new SchemaReleaseResolver(client, NullLogger<SchemaReleaseResolver>.Instance),
                NullLogger<BaselineLocationResolver>.Instance),
            new FileHelper(fs), NullLogger<BaselineLoader>.Instance, new ServerStatusRecorder(),
            new NullCrossProcessLock());

        var index = new FakeGameIndexService();
        var applications = 0;
        index.DynamicEnumChanged += _ => applications++;

        var config = new FakeLspConfigurationProvider
        {
            Current = new LspConfiguration
            {
                BaselineSource = new BaselineSourceConfig { Type = BaselineSourceType.Local, LocalPath = BaselineDir }
            }
        };

        var bootstrapper = new BaselineBootstrapper(config, index, loader,
            sp.GetRequiredService<IBaselineTranslationProvider>(), sp.GetRequiredService<ILanguageService>(),
            NullLogger<BaselineBootstrapper>.Instance);
        return (bootstrapper, index, () => applications);
    }

    private static byte[] Baseline(string onlySymbol)
    {
        return BaselineSerializer.Serialize(new BaselineIndex(
            ImmutableDictionary<string, GameSymbol>.Empty.Add(onlySymbol,
                new GameSymbol(onlySymbol, GameSymbolKind.XmlObject, "GameObjectType",
                    new FileOrigin("data\\xml\\units.xml", 0, null), null)),
            DateTimeOffset.UtcNow, "testhash",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty));
    }

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(respond(request));
        }
    }
}
