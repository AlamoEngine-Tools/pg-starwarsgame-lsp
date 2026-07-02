// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.Files.DAT.Services;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Dat;
using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
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

        var (loader, indexService, _, _) = BuildLoader(fs, config);
        await loader.LoadAsync(WorkspaceConfiguration.Empty, CancellationToken.None);

        Assert.True(indexService.Current.Localisation.ContainsKey("TEXT_MY_UNIT_NAME"));
    }

    [Fact]
    public async Task LoadAsync_NoLocalisationFilesFound_DoesNotThrowAndIndexIsNonNull()
    {
        var config = new LspConfiguration
        {
            Localisation = new LocalisationConfig { ResourceType = "Csv" }
        };

        var (loader, indexService, _, _) = BuildLoader(new MockFileSystem(), config);
        await loader.LoadAsync(WorkspaceConfiguration.Empty, CancellationToken.None);

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

        var (loader, indexService, _, _) = BuildLoader(fs, config);
        var ex = await Record.ExceptionAsync(() =>
            loader.LoadAsync(WorkspaceConfiguration.Empty, CancellationToken.None));

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

        var (loader, indexService, _, _) = BuildLoader(fs, config);
        await loader.LoadAsync(WorkspaceConfiguration.Empty, CancellationToken.None);

        Assert.True(indexService.Current.Localisation.ContainsKey("KEY_A"));
        Assert.True(indexService.Current.Localisation.ContainsKey("KEY_B"));
    }

    [Fact]
    public async Task LoadAsync_TextRootsPopulated_UsesTextRootsAndResourceType()
    {
        // pgproj mode: TextRoots + TextResourceType replace SourcePaths / locConfig.ResourceType.
        const string csvPath = "/mod/text/localisation.csv";
        const string csvContent = "key,ENGLISH\nTEXT_PGPROJ_KEY,Hello";

        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [csvPath] = new(csvContent)
        });

        var config = new LspConfiguration
        {
            // User config has no source paths and a different resource type — both must be ignored.
            Localisation = new LocalisationConfig { ResourceType = "Dat", SourcePaths = [] }
        };

        var workspaceConfig = new WorkspaceConfiguration([], [], ["/mod/text"], [], "Csv");

        var (loader, indexService, _, _) = BuildLoader(fs, config);
        await loader.LoadAsync(workspaceConfig, CancellationToken.None);

        Assert.True(indexService.Current.Localisation.ContainsKey("TEXT_PGPROJ_KEY"));
    }

    [Fact]
    public async Task LoadAsync_TextRootsPopulated_NoTextResourceType_FallsBackToUserResourceType()
    {
        // TextRoots set but no TextResourceType → use locConfig.ResourceType for the extension filter.
        const string csvPath = "/mod/text/localisation.csv";
        const string csvContent = "key,ENGLISH\nTEXT_FALLBACK_TYPE,Hello";

        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [csvPath] = new(csvContent)
        });

        var config = new LspConfiguration
        {
            Localisation = new LocalisationConfig { ResourceType = "Csv" }
        };

        var workspaceConfig = new WorkspaceConfiguration([], [], ["/mod/text"], [], null);

        var (loader, indexService, _, _) = BuildLoader(fs, config);
        await loader.LoadAsync(workspaceConfig, CancellationToken.None);

        Assert.True(indexService.Current.Localisation.ContainsKey("TEXT_FALLBACK_TYPE"));
    }

    [Fact]
    public async Task LoadAsync_TextRootsEmpty_IgnoresTextResourceType_UsesUserConfig()
    {
        // Heuristic mode: TextRoots empty → user's SourcePaths and ResourceType apply unchanged.
        const string csvPath = "/mod/text/localisation.csv";
        const string csvContent = "key,ENGLISH\nTEXT_HEURISTIC_KEY,Hello";

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

        // TextRoots empty — workspace TextResourceType should be ignored.
        var workspaceConfig = new WorkspaceConfiguration([], [], [], [], "Dat");

        var (loader, indexService, _, _) = BuildLoader(fs, config);
        await loader.LoadAsync(workspaceConfig, CancellationToken.None);

        Assert.True(indexService.Current.Localisation.ContainsKey("TEXT_HEURISTIC_KEY"));
    }

    // ── layered (project-reference) localisation ─────────────────────────────

    [Fact]
    public async Task LoadAsync_LayeredProjects_DependencyCsvLoaded_EvenWhenRootResourceTypeDiffers()
    {
        // The "missing texts" bug: the dependency ships CSV, the root uses a different type.
        // The dependency's CSV must still be imported (as CSV), not skipped.
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/dep/text/core.csv"] = new("key,ENGLISH\nTEXT_DEP_KEY,From Core")
        });

        var config = new LspConfiguration
        {
            Localisation = new LocalisationConfig { ResourceType = "Csv" }
        };

        var workspaceConfig = WorkspaceConfiguration.Empty with
        {
            Layers =
            [
                new ProjectLayer(0, "Core", [], [], ["/dep/text"], [], "Csv"),
                new ProjectLayer(1, "Root", [], [], ["/rev/text"], [], "Xml")
            ]
        };

        var (loader, indexService, _, _) = BuildLoader(fs, config);
        await loader.LoadAsync(workspaceConfig, CancellationToken.None);

        Assert.True(indexService.Current.Localisation.ContainsKey("TEXT_DEP_KEY"));
    }

    [Fact]
    public async Task LoadAsync_LayeredProjects_HigherLayerTranslationWins()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/dep/text/core.csv"] = new("key,ENGLISH\nTEXT_SHARED,From Core"),
            ["/rev/text/rev.csv"] = new("key,ENGLISH\nTEXT_SHARED,From Rev")
        });

        var config = new LspConfiguration
        {
            Localisation = new LocalisationConfig { ResourceType = "Csv" }
        };

        var workspaceConfig = WorkspaceConfiguration.Empty with
        {
            Layers =
            [
                new ProjectLayer(0, "Core", [], [], ["/dep/text"], [], "Csv"),
                new ProjectLayer(1, "Root", [], [], ["/rev/text"], [], "Csv")
            ]
        };

        var (loader, indexService, _, _) = BuildLoader(fs, config);
        await loader.LoadAsync(workspaceConfig, CancellationToken.None);

        Assert.Equal("From Rev", indexService.Current.Localisation.GetValue("TEXT_SHARED"));
    }

    // ── DAT resource type ─────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_DatResourceType_ImportsRealDatContent()
    {
        var fs = new MockFileSystem();
        var fixtureSp = BuildDatFixtureServiceProvider(fs);
        var english = fixtureSp.GetRequiredService<ILanguageService>().Default;
        WriteDatFixture(fs, fixtureSp, "/mod/text/MasterTextFile_ENGLISH.dat", english,
            "TEXT_DAT_KEY", "Dat Value");

        var config = new LspConfiguration
        {
            Localisation = new LocalisationConfig { ResourceType = "Csv" }
        };
        var workspaceConfig = new WorkspaceConfiguration([], [], ["/mod/text"], [], "Dat");

        var (loader, indexService, _, _) = BuildLoader(fs, config);
        await loader.LoadAsync(workspaceConfig, CancellationToken.None);

        Assert.Equal("Dat Value", indexService.Current.Localisation.GetValue("TEXT_DAT_KEY"));
    }

    [Fact]
    public async Task LoadAsync_DatResourceType_UnresolvableLanguageFileName_SkippedNotFatal()
    {
        var fs = new MockFileSystem();
        var fixtureSp = BuildDatFixtureServiceProvider(fs);
        var english = fixtureSp.GetRequiredService<ILanguageService>().Default;
        WriteDatFixture(fs, fixtureSp, "/mod/text/MasterTextFile_ENGLISH.dat", english,
            "TEXT_GOOD", "Good Value");
        fs.AddFile("/mod/text/MasterTextFile_NOTALANGUAGE.dat", new MockFileData(new byte[] { 9, 9, 9 }));

        var config = new LspConfiguration
        {
            Localisation = new LocalisationConfig { ResourceType = "Csv" }
        };
        var workspaceConfig = new WorkspaceConfiguration([], [], ["/mod/text"], [], "Dat");

        var (loader, indexService, _, _) = BuildLoader(fs, config);
        var ex = await Record.ExceptionAsync(() => loader.LoadAsync(workspaceConfig, CancellationToken.None));

        Assert.Null(ex);
        Assert.Equal("Good Value", indexService.Current.Localisation.GetValue("TEXT_GOOD"));
    }

    // ── registry-population tests ────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_CsvFileAtExplicitSourcePath_RegistryContainsProject()
    {
        const string csvPath = "/mod/text/my_text.csv";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [csvPath] = new("key,ENGLISH\nTEXT_X,X")
        });
        var config = new LspConfiguration
        {
            Localisation = new LocalisationConfig { ResourceType = "Csv", SourcePaths = [csvPath] }
        };

        var (loader, _, registry, _) = BuildLoader(fs, config);
        await loader.LoadAsync(WorkspaceConfiguration.Empty, CancellationToken.None);

        Assert.Single(registry.Projects);
        Assert.Equal(csvPath, registry.Projects[0].FilePath);
        Assert.Equal("Csv", registry.Projects[0].ResourceType);
    }

    [Fact]
    public async Task LoadAsync_MultipleSourcePaths_RegistryContainsAllProjects()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/text/a.csv"] = new("key,ENGLISH\nKEY_A,A"),
            ["/mod/text/b.csv"] = new("key,ENGLISH\nKEY_B,B")
        });
        var config = new LspConfiguration
        {
            Localisation = new LocalisationConfig
            {
                ResourceType = "Csv",
                SourcePaths = ["/mod/text/a.csv", "/mod/text/b.csv"]
            }
        };

        var (loader, _, registry, _) = BuildLoader(fs, config);
        await loader.LoadAsync(WorkspaceConfiguration.Empty, CancellationToken.None);

        Assert.Equal(2, registry.Projects.Count);
    }

    [Fact]
    public async Task LoadAsync_NoLocalisationFilesFound_RegistryIsEmpty()
    {
        var config = new LspConfiguration
        {
            Localisation = new LocalisationConfig { ResourceType = "Csv" }
        };

        var (loader, _, registry, _) = BuildLoader(new MockFileSystem(), config);
        await loader.LoadAsync(WorkspaceConfiguration.Empty, CancellationToken.None);

        Assert.Empty(registry.Projects);
    }

    [Fact]
    public async Task LoadAsync_TextRootsMode_RegistryContainsDiscoveredFile()
    {
        const string csvPath = "/mod/text/localisation.csv";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [csvPath] = new("key,ENGLISH\nTEXT_PGPROJ_KEY,Hello")
        });
        var config = new LspConfiguration
        {
            Localisation = new LocalisationConfig { ResourceType = "Dat", SourcePaths = [] }
        };
        var workspaceConfig = new WorkspaceConfiguration([], [], ["/mod/text"], [], "Csv");

        var (loader, _, registry, _) = BuildLoader(fs, config);
        await loader.LoadAsync(workspaceConfig, CancellationToken.None);

        Assert.Single(registry.Projects);
        Assert.Equal("localisation.csv", Path.GetFileName(registry.Projects[0].FilePath));
    }

    [Fact]
    public async Task LoadAsync_LayeredProjects_ProjectRegistryEntriesCarryProjectNameAndRank()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/dep/text/core.csv"] = new("key,ENGLISH\nTEXT_DEP_KEY,From Core"),
            ["/rev/text/rev.csv"] = new("key,ENGLISH\nTEXT_ROOT_KEY,From Root")
        });
        var config = new LspConfiguration { Localisation = new LocalisationConfig { ResourceType = "Csv" } };
        var workspaceConfig = WorkspaceConfiguration.Empty with
        {
            Layers =
            [
                new ProjectLayer(0, "Core", [], [], ["/dep/text"], [], "Csv"),
                new ProjectLayer(1, "Root", [], [], ["/rev/text"], [], "Csv")
            ]
        };

        var (loader, _, registry, _) = BuildLoader(fs, config);
        await loader.LoadAsync(workspaceConfig, CancellationToken.None);

        var depProject = Assert.Single(registry.Projects, p => p.ProjectName == "Core");
        Assert.Equal(0, depProject.Rank);
        var rootProject = Assert.Single(registry.Projects, p => p.ProjectName == "Root");
        Assert.Equal(1, rootProject.Rank);
    }

    [Fact]
    public async Task LoadAsync_LayeredProjects_LayerRegistryPopulatedWithOneEntryPerLayer()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/dep/text/core.csv"] = new("key,ENGLISH\nTEXT_DEP_KEY,From Core"),
            ["/rev/text/rev.csv"] = new("key,ENGLISH\nTEXT_ROOT_KEY,From Root")
        });
        var config = new LspConfiguration { Localisation = new LocalisationConfig { ResourceType = "Csv" } };
        var workspaceConfig = WorkspaceConfiguration.Empty with
        {
            Layers =
            [
                new ProjectLayer(0, "Core", [], [], ["/dep/text"], [], "Csv"),
                new ProjectLayer(1, "Root", [], [], ["/rev/text"], [], "Csv")
            ]
        };

        var (loader, _, _, layerRegistry) = BuildLoader(fs, config);
        await loader.LoadAsync(workspaceConfig, CancellationToken.None);

        Assert.Equal(2, layerRegistry.Layers.Count);
        var depEntry = Assert.Single(layerRegistry.Layers, e => e.Layer.Name == "Core");
        Assert.True(depEntry.Database.ContainsKey("TEXT_DEP_KEY"));
        var rootEntry = Assert.Single(layerRegistry.Layers, e => e.Layer.Name == "Root");
        Assert.True(rootEntry.Database.ContainsKey("TEXT_ROOT_KEY"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (LocalisationLoader loader, IGameIndexService indexService,
        LocalisationProjectRegistry registry, LocalisationLayerRegistry layerRegistry) BuildLoader(
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
        services.AddSingleton<LocalisationProjectRegistry>();
        services.AddSingleton<ILocalisationProjectRegistry>(sp => sp.GetRequiredService<LocalisationProjectRegistry>());
        services.AddSingleton<LocalisationLayerRegistry>();

        var sp = services.BuildServiceProvider();
        var loader = ActivatorUtilities.CreateInstance<LocalisationLoader>(sp);
        return (loader, sp.GetRequiredService<IGameIndexService>(),
            sp.GetRequiredService<LocalisationProjectRegistry>(),
            sp.GetRequiredService<LocalisationLayerRegistry>());
    }

    private static IServiceProvider BuildDatFixtureServiceProvider(MockFileSystem fs)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.SupportLocalisationBaseline();
        return services.BuildServiceProvider();
    }

    private static void WriteDatFixture(
        MockFileSystem fs, IServiceProvider sp, string path,
        IAlamoLanguageDefinition language, string key, string value)
    {
        var factory = sp.GetRequiredService<ITranslationDatabaseFactory>();
        var exporter = sp.GetRequiredService<IDatTranslationExporter>();
        var datFileService = sp.GetRequiredService<IDatFileService>();

        var db = factory.CreateKeyed([language]);
        db.SetTranslation(key, language, value);
        var model = exporter.Export(db, language);

        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(path)!);
        using var stream = fs.File.Create(path);
        datFileService.CreateDatFile(stream, model, model.KeySortOrder);
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