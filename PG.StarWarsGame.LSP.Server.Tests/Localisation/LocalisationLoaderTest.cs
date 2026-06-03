// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class LocalisationLoaderTest
{
    // ── LocalisationConfig defaults ──────────────────────────────────────────

    [Fact]
    public void LocalisationConfig_Default_ResourceTypeIsCsv()
    {
        Assert.Equal("Csv", new LocalisationConfig().ResourceType);
    }

    [Fact]
    public void LocalisationConfig_Default_SourcePathsIsEmpty()
    {
        Assert.Empty(new LocalisationConfig().SourcePaths);
    }

    [Fact]
    public void LspConfiguration_Default_HasLocalisationConfig()
    {
        Assert.NotNull(new LspConfiguration().Localisation);
    }

    // ── LocalisationLoader ───────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_CsvFileAtExplicitSourcePath_WorkspaceKeyAppearsInIndex()
    {
        const string csvPath = "/mod/text/my_text.csv";
        const string csvContent = "key,ENGLISH\nTEXT_MY_UNIT_NAME,X-Wing Fighter";

        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [csvPath] = new(csvContent)
        });

        var config = new LspConfiguration
        {
            Localisation = new LocalisationConfig
            {
                ResourceType = "Csv",
                SourcePaths = [csvPath]
            }
        };

        var (loader, indexService) = BuildLoader(fs, config);
        await loader.LoadAsync(CancellationToken.None);

        Assert.True(indexService.Current.Localisation.ContainsKey("TEXT_MY_UNIT_NAME"));
    }

    [Fact]
    public async Task LoadAsync_CsvFileAutoDetectedFromModPath_WorkspaceKeyAppearsInIndex()
    {
        const string csvPath = "/mod/Data/Text/localisation.csv";
        const string csvContent = "key,ENGLISH\nTEXT_AUTO_DETECT_KEY,Some Value";

        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [csvPath] = new(csvContent)
        });

        var config = new LspConfiguration
        {
            ModPaths = ["/mod"],
            Localisation = new LocalisationConfig { ResourceType = "Csv" }
        };

        var (loader, indexService) = BuildLoader(fs, config);
        await loader.LoadAsync(CancellationToken.None);

        Assert.True(indexService.Current.Localisation.ContainsKey("TEXT_AUTO_DETECT_KEY"));
    }

    [Fact]
    public async Task LoadAsync_NoLocalisationFilesFound_DoesNotThrowAndIndexIsNonNull()
    {
        var config = new LspConfiguration
        {
            ModPaths = ["/mod"],
            Localisation = new LocalisationConfig { ResourceType = "Csv" }
        };

        var (loader, indexService) = BuildLoader(new MockFileSystem(), config);
        await loader.LoadAsync(CancellationToken.None);

        Assert.NotNull(indexService.Current.Localisation);
    }

    [Fact]
    public async Task LoadAsync_MalformedCsvFile_DoesNotThrow()
    {
        const string csvPath = "/mod/text/bad.csv";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [csvPath] = new("this is not valid csv at all\x00\x01\x02")
        });

        var config = new LspConfiguration
        {
            Localisation = new LocalisationConfig
            {
                ResourceType = "Csv",
                SourcePaths = [csvPath]
            }
        };

        var (loader, indexService) = BuildLoader(fs, config);
        var ex = await Record.ExceptionAsync(() => loader.LoadAsync(CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task LoadAsync_MultipleCsvFiles_AllKeysAppearInIndex()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/text/a.csv"] = new("key,ENGLISH\nKEY_A,Value A"),
            ["/mod/text/b.csv"] = new("key,ENGLISH\nKEY_B,Value B")
        });

        var config = new LspConfiguration
        {
            Localisation = new LocalisationConfig
            {
                ResourceType = "Csv",
                SourcePaths = ["/mod/text/a.csv", "/mod/text/b.csv"]
            }
        };

        var (loader, indexService) = BuildLoader(fs, config);
        await loader.LoadAsync(CancellationToken.None);

        Assert.True(indexService.Current.Localisation.ContainsKey("KEY_A"));
        Assert.True(indexService.Current.Localisation.ContainsKey("KEY_B"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (LocalisationLoader loader, IGameIndexService indexService) BuildLoader(
        IFileSystem fs, LspConfiguration config)
    {
        var services = new ServiceCollection();

        // Register mock file system BEFORE SupportLocalisationBaseline so TryAddSingleton skips it.
        services.AddSingleton(fs);
        services.SupportLocalisationBaseline();

        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.AddSingleton<ILspConfigurationProvider>(new StubConfigProvider(config));
        services.AddSingleton<IGameIndexService>(sp =>
            new GameIndexService(sp.GetRequiredService<IFileHelper>(), [],
                NullLogger<GameIndexService>.Instance));
        services.AddSingleton<ILogger<LocalisationLoader>>(NullLogger<LocalisationLoader>.Instance);

        var sp = services.BuildServiceProvider();
        var loader = ActivatorUtilities.CreateInstance<LocalisationLoader>(sp);
        return (loader, sp.GetRequiredService<IGameIndexService>());
    }
}

file sealed class StubConfigProvider : ILspConfigurationProvider
{
    public StubConfigProvider(LspConfiguration config)
    {
        Current = config;
    }

    public LspConfiguration Current { get; }

    public void LoadFrom(object? options)
    {
    }
}