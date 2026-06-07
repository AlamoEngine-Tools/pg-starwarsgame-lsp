// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

public sealed class ModProjectReloadServiceTest
{
    private static readonly string DriveRoot = Path.GetPathRoot(Path.GetFullPath("."))!;
    private static readonly string WorkspaceRoot = Path.Combine(DriveRoot, "mods", "mymod");
    private static readonly string ProjectPath = Path.Combine(WorkspaceRoot, "mymod.pgproj");

    private static string AbsLower(string rel)
    {
        return Path.GetFullPath(Path.Combine(WorkspaceRoot, rel)).Replace('\\', '/').ToLowerInvariant();
    }

    [Fact]
    public async Task LoadAsync_NoProjectFile_UsesHeuristicConfig_AndWarns()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(WorkspaceRoot);
        var (service, scanner, logger) = Build(fs);

        await service.LoadAsync([WorkspaceRoot], CancellationToken.None);

        Assert.NotNull(scanner.LastScannedConfig);
        Assert.Empty(scanner.LastScannedConfig!.XmlDirectories);
        Assert.Equal([WorkspaceRoot], scanner.LastScannedConfig.ScriptRoots);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("heuristic"));
    }

    [Fact]
    public async Task LoadAsync_ProjectFileFound_UsesResolvedConfig()
    {
        const string json = """
            {
              "modinfo": { "name": "My Mod" },
              "directories": { "xml": ["data/xml"], "scripts": ["data/scripts"] }
            }
            """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectPath] = new(json)
        });
        var (service, scanner, _) = Build(fs);

        await service.LoadAsync([WorkspaceRoot], CancellationToken.None);

        Assert.NotNull(scanner.LastScannedConfig);
        Assert.Contains(AbsLower("data/xml"), scanner.LastScannedConfig!.XmlDirectories);
        Assert.Contains(AbsLower("data/scripts"), scanner.LastScannedConfig.ScriptRoots);
    }

    [Fact]
    public async Task ReloadAsync_BeforeLoadAsync_NoOpWithWarningLog()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(WorkspaceRoot);
        var (service, scanner, logger) = Build(fs);

        await service.ReloadAsync(CancellationToken.None);

        Assert.Null(scanner.LastScannedConfig);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task LoadAsync_MalformedProjectFile_FallsBackToHeuristic()
    {
        const string malformed = "{ this is not valid json ";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectPath] = new(malformed)
        });
        var (service, scanner, _) = Build(fs);

        await service.LoadAsync([WorkspaceRoot], CancellationToken.None);

        Assert.NotNull(scanner.LastScannedConfig);
        Assert.Empty(scanner.LastScannedConfig!.XmlDirectories);
        Assert.Equal([WorkspaceRoot], scanner.LastScannedConfig.ScriptRoots);
    }

    private static (ModProjectReloadService Service, WorkspaceScanner Scanner, ListLogger Logger) Build(
        MockFileSystem fs)
    {
        var fileHelper = new FileHelper(fs);
        var scanner = new WorkspaceScanner(
            fileHelper,
            [],
            new FakeIndexService(),
            new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance),
            NullLogger<WorkspaceScanner>.Instance,
            null,
            new FileTypeRegistry(),
            new FakeSchemaProvider(),
            new EaWXmlContext(fileHelper),
            new PreOpenBuffer(),
            new NullLocalisationLoader());

        var loader = new ModProjectLoader(fileHelper, NullLogger<ModProjectLoader>.Instance);
        var graph = new ProjectDependencyGraph(NullLogger<ProjectDependencyGraph>.Instance);
        var resolver = new ModProjectResolver(fileHelper, loader, graph,
            NullLogger<ModProjectResolver>.Instance);
        var detector = new ModProjectDetector(fileHelper, NullLogger<ModProjectDetector>.Instance);
        var logger = new ListLogger();

        var service = new ModProjectReloadService(detector, loader, resolver, scanner, logger);
        return (service, scanner, logger);
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class ListLogger : ILogger<ModProjectReloadService>
    {
        public ConcurrentBag<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed class FakeSchemaProvider : ISchemaProvider
    {
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];

        // Fire immediately on subscription so WaitForSchemaAsync does not time out.
        public event EventHandler? SchemaRefreshed
        {
            add { value?.Invoke(this, EventArgs.Empty); }
            remove { }
        }

        public XmlTagDefinition? GetTag(string tagName) => null;
        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName) => [];
        public GameObjectTypeDefinition? GetObjectType(string typeName) => null;
        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName) => [];
        public EnumDefinition? GetEnum(string enumName) => null;
    }

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current => GameIndex.Empty;

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
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

        public IDisposable BeginBulkUpdate() => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class NullLocalisationLoader : ILocalisationLoader
    {
        public Task LoadAsync(WorkspaceConfiguration workspaceConfig, CancellationToken ct) => Task.CompletedTask;
    }
}
