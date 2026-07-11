// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class GameDidChangeWatchedFilesHandlerTest
{
    private const string XmlUri = "file:///c:/data/units.xml";
    private const string LuaUri = "file:///c:/scripts/mission.lua";

    // ── helpers ────────────────────────────────────────────────────────────────

    private static DidChangeWatchedFilesParams Changed(params string[] uris)
    {
        return new DidChangeWatchedFilesParams
        {
            Changes = new Container<FileEvent>(uris.Select(u =>
                new FileEvent { Uri = DocumentUri.From(u), Type = FileChangeType.Changed }))
        };
    }

    private static DidChangeWatchedFilesParams Deleted(params string[] uris)
    {
        return new DidChangeWatchedFilesParams
        {
            Changes = new Container<FileEvent>(uris.Select(u =>
                new FileEvent { Uri = DocumentUri.From(u), Type = FileChangeType.Deleted }))
        };
    }

    private static GameDidChangeWatchedFilesHandler BuildHandler(
        SpyIndexService? index = null,
        FakeWorkspaceHost? host = null,
        MockFileSystem? fs = null,
        FakeReloadService? reload = null,
        ISchemaProvider? schema = null)
    {
        var fileSystem = fs ?? new MockFileSystem();
        var fileHelper = new FileHelper(fileSystem);
        var idx = index ?? new SpyIndexService();
        var schemaProvider = schema ?? new FakeSchemaProvider();
        var indexer = new WorkspaceIndexer(
            fileHelper,
            [],
            idx,
            new FileTypeRegistry(),
            schemaProvider,
            new EaWXmlContext(fileHelper),
            new NullProjectIndexCache(),
            new LuaAnnotationRepository(),
            new StoryChainProblemStore(),
            new FakeLspConfigurationProvider(),
            NullLogger<WorkspaceIndexer>.Instance);
        return new GameDidChangeWatchedFilesHandler(
            idx,
            host ?? new FakeWorkspaceHost(),
            fileHelper,
            indexer,
            reload ?? new FakeReloadService(),
            schemaProvider,
            NullLogger<GameDidChangeWatchedFilesHandler>.Instance);
    }

    // ── changed file not open in editor ──────────────────────────────────────

    [Fact]
    public async Task Handle_ChangedFile_NotInWorkspaceHost_ReIndexesFromDisk()
    {
        const string path = @"c:\data\units.xml";
        const string freshContent = "<GameObjectFiles><Unit Name=\"NEW_UNIT\"/></GameObjectFiles>";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new(freshContent)
        });
        var spy = new SpyIndexService();

        var handler = BuildHandler(spy, fs: fs);
        await handler.Handle(Changed(XmlUri), CancellationToken.None);

        Assert.Contains(spy.Updates, u => u.Uri == "file:///c:/data/units.xml" && u.Text == freshContent);
    }

    [Fact]
    public async Task Handle_ChangedFile_OpenInEditor_DoesNotReIndex()
    {
        const string path = @"c:\scripts\mission.lua";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new("-- stale disk content")
        });
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "-- live editor content", 5);
        var spy = new SpyIndexService();

        var handler = BuildHandler(spy, host, fs);
        await handler.Handle(Changed(LuaUri), CancellationToken.None);

        Assert.Empty(spy.Updates);
    }

    // ── deleted file ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_DeletedFile_RemovesFromIndex()
    {
        var spy = new SpyIndexService();

        var handler = BuildHandler(spy);
        await handler.Handle(Deleted(XmlUri), CancellationToken.None);

        Assert.Contains(spy.Removals, r => r == "file:///c:/data/units.xml");
        Assert.Empty(spy.Updates);
    }

    // ── file not found on disk ────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ChangedFile_NotOnDisk_IsIgnored()
    {
        var spy = new SpyIndexService();

        var handler = BuildHandler(spy, fs: new MockFileSystem()); // empty fs
        await handler.Handle(Changed(XmlUri), CancellationToken.None);

        Assert.Empty(spy.Updates);
        Assert.Empty(spy.Removals);
    }

    // ── multiple changes ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_MultipleChanges_AllClosedFilesReIndexed()
    {
        const string xmlPath = @"c:\data\units.xml";
        const string luaPath = @"c:\scripts\mission.lua";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [xmlPath] = new("<root/>"),
            [luaPath] = new("-- script")
        });
        var spy = new SpyIndexService();

        var request = new DidChangeWatchedFilesParams
        {
            Changes = new Container<FileEvent>(
                new FileEvent { Uri = DocumentUri.From(XmlUri), Type = FileChangeType.Changed },
                new FileEvent { Uri = DocumentUri.From(LuaUri), Type = FileChangeType.Changed })
        };
        var handler = BuildHandler(spy, fs: fs);
        await handler.Handle(request, CancellationToken.None);

        Assert.Equal(2, spy.Updates.Count);
    }

    // ── .pgproj changes ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_PgprojChanged_TriggersReload_AndDoesNotReIndex()
    {
        const string pgprojUri = "file:///c:/mods/mymod/mymod.pgproj";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\mymod.pgproj"] = new("{}")
        });
        var spy = new SpyIndexService();
        var reload = new FakeReloadService();

        var handler = BuildHandler(spy, fs: fs, reload: reload);
        await handler.Handle(Changed(pgprojUri), CancellationToken.None);

        Assert.Equal(1, reload.ReloadCount);
        Assert.Empty(spy.Updates);
    }

    [Fact]
    public async Task Handle_PgprojDeleted_TriggersReload()
    {
        const string pgprojUri = "file:///c:/mods/mymod/mymod.pgproj";
        var spy = new SpyIndexService();
        var reload = new FakeReloadService();

        var handler = BuildHandler(spy, reload: reload);
        await handler.Handle(Deleted(pgprojUri), CancellationToken.None);

        Assert.Equal(1, reload.ReloadCount);
        Assert.Empty(spy.Removals);
    }

    // ── asset changes ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_AssetFileChanged_DoesNotReIndexAsDocument()
    {
        const string assetUri = "file:///c:/data/art/textures/foo.tga";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\data\art\textures\foo.tga"] = new("")
        });
        var spy = new SpyIndexService();

        var handler = BuildHandler(spy, fs: fs);
        await handler.Handle(Changed(assetUri), CancellationToken.None);

        Assert.Empty(spy.Updates);
    }

    // ── dynamic enum source file changes ─────────────────────────────────────────

    [Fact]
    public async Task Handle_DynamicEnumSourceFileChanged_RefreshesEnumCatalog_NotDocumentIndex()
    {
        const string xmlUri = "file:///c:/mods/mymod/data/xml/gameconstants.xml";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\data\xml\gameconstants.xml"] =
                new("<GameConstants><Armor_Types>Armor_Structure ArmourG_Structure</Armor_Types></GameConstants>")
        });
        var spy = new SpyIndexService();
        var schema = new FakeSchemaProvider(new EnumDefinition
        {
            Name = "ArmorType", Kind = EnumKind.DynamicXml,
            SourceFile = "data/xml/gameconstants.xml$Armor_Types", Values = []
        });
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration(["c:/mods/mymod/data/xml"], [], [], [], "csv")
        };

        var handler = BuildHandler(spy, fs: fs, reload: reload, schema: schema);
        await handler.Handle(Changed(xmlUri), CancellationToken.None);

        Assert.Single(spy.DynamicEnumApplications);
        Assert.Contains("ArmourG_Structure", spy.DynamicEnumApplications[0]["ArmorType"]);
        Assert.Empty(spy.Updates);
    }

    [Fact]
    public async Task Handle_DynamicEnumSourceFileChanged_NoWorkspaceConfigYet_DoesNotThrow()
    {
        const string xmlUri = "file:///c:/mods/mymod/data/xml/gameconstants.xml";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\data\xml\gameconstants.xml"] = new("<GameConstants/>")
        });
        var spy = new SpyIndexService();
        var schema = new FakeSchemaProvider(new EnumDefinition
        {
            Name = "ArmorType", Kind = EnumKind.DynamicXml,
            SourceFile = "data/xml/gameconstants.xml$Armor_Types", Values = []
        });

        var handler = BuildHandler(spy, fs: fs, reload: new FakeReloadService(), schema: schema);
        var ex = await Record.ExceptionAsync(() => handler.Handle(Changed(xmlUri), CancellationToken.None));

        Assert.Null(ex);
    }

    // ── localisation text-root changes ───────────────────────────────────────────

    [Fact]
    public async Task Handle_CsvUnderTextRoot_TriggersLocalisationReload_NotDocumentIndex()
    {
        const string csvUri = "file:///c:/mods/mymod/data/text/mastertextfile.csv";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\data\text\mastertextfile.csv"] = new("key,ENGLISH\r\nTEXT_A,Hello\r\n")
        });
        var spy = new SpyIndexService();
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration([], [], ["c:/mods/mymod/data/text"], [], "csv")
        };

        var handler = BuildHandler(spy, fs: fs, reload: reload);
        await handler.Handle(Changed(csvUri), CancellationToken.None);

        Assert.Equal(1, reload.LocalisationReloadCount);
        Assert.Empty(spy.Updates);
    }

    [Fact]
    public async Task Handle_PropertiesUnderTextRoot_TriggersLocalisationReload()
    {
        const string propsUri = "file:///c:/mods/mymod/data/text/mastertextfile.properties";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\data\text\mastertextfile.properties"] = new("TEXT_A=Hello")
        });
        var spy = new SpyIndexService();
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration([], [], ["c:/mods/mymod/data/text"], [], "nls")
        };

        var handler = BuildHandler(spy, fs: fs, reload: reload);
        await handler.Handle(Changed(propsUri), CancellationToken.None);

        Assert.Equal(1, reload.LocalisationReloadCount);
    }

    [Fact]
    public async Task Handle_XmlUnderTextRoot_RoutesToLocalisationReload_NotDocumentParser()
    {
        // A localisation XML file matches the blanket **/*.xml watcher too — it must be routed to
        // the localisation reload, not the game-XML document parser (no parser understands it).
        const string xmlUri = "file:///c:/mods/mymod/data/text/mastertextfile.xml";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\data\text\mastertextfile.xml"] = new("<LocalisationData/>")
        });
        var spy = new SpyIndexService();
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration([], [], ["c:/mods/mymod/data/text"], [], "xml")
        };

        var handler = BuildHandler(spy, fs: fs, reload: reload);
        await handler.Handle(Changed(xmlUri), CancellationToken.None);

        Assert.Equal(1, reload.LocalisationReloadCount);
        Assert.Empty(spy.Updates);
    }

    [Fact]
    public async Task Handle_DeletedCsvUnderTextRoot_TriggersLocalisationReload_NotRemoveDocument()
    {
        const string csvUri = "file:///c:/mods/mymod/data/text/mastertextfile.csv";
        var spy = new SpyIndexService();
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration([], [], ["c:/mods/mymod/data/text"], [], "csv")
        };

        var handler = BuildHandler(spy, reload: reload);
        await handler.Handle(Deleted(csvUri), CancellationToken.None);

        Assert.Equal(1, reload.LocalisationReloadCount);
        Assert.Empty(spy.Removals);
    }

    [Fact]
    public async Task Handle_MultipleTextRootChanges_TriggersExactlyOneLocalisationReload()
    {
        const string csvUri = "file:///c:/mods/mymod/data/text/a.csv";
        const string propsUri = "file:///c:/mods/mymod/data/text/b.properties";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\data\text\a.csv"] = new("key,ENGLISH"),
            [@"c:\mods\mymod\data\text\b.properties"] = new("")
        });
        var spy = new SpyIndexService();
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration([], [], ["c:/mods/mymod/data/text"], [], "csv")
        };

        var request = new DidChangeWatchedFilesParams
        {
            Changes = new Container<FileEvent>(
                new FileEvent { Uri = DocumentUri.From(csvUri), Type = FileChangeType.Changed },
                new FileEvent { Uri = DocumentUri.From(propsUri), Type = FileChangeType.Changed })
        };
        var handler = BuildHandler(spy, fs: fs, reload: reload);
        await handler.Handle(request, CancellationToken.None);

        Assert.Equal(1, reload.LocalisationReloadCount);
    }

    [Fact]
    public async Task Handle_CsvOutsideAnyTextRoot_NotRoutedToLocalisationReload()
    {
        const string csvUri = "file:///c:/mods/mymod/notes/scratch.csv";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\notes\scratch.csv"] = new("a,b")
        });
        var spy = new SpyIndexService();
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration([], [], ["c:/mods/mymod/data/text"], [], "csv")
        };

        var handler = BuildHandler(spy, fs: fs, reload: reload);
        await handler.Handle(Changed(csvUri), CancellationToken.None);

        Assert.Equal(0, reload.LocalisationReloadCount);
    }

    [Fact]
    public async Task Handle_CsvChanged_NoWorkspaceConfigYet_NotRoutedToLocalisationReload()
    {
        // LastWorkspaceConfig is null before the first successful LoadAsync — the handler must not
        // throw, and the change is simply ignored as an unrecognised document.
        const string csvUri = "file:///c:/mods/mymod/data/text/mastertextfile.csv";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\data\text\mastertextfile.csv"] = new("key,ENGLISH")
        });
        var spy = new SpyIndexService();
        var reload = new FakeReloadService();

        var handler = BuildHandler(spy, fs: fs, reload: reload);
        var ex = await Record.ExceptionAsync(() => handler.Handle(Changed(csvUri), CancellationToken.None));

        Assert.Null(ex);
        Assert.Equal(0, reload.LocalisationReloadCount);
    }

    // ── batching: expensive reactions run once per notification ────────────────

    [Fact]
    public async Task Handle_MultipleAssetChanges_ReglobsAssetCatalogOnce()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\data\art\textures\a.tga"] = new(""),
            [@"c:\data\art\textures\b.dds"] = new(""),
            [@"c:\data\art\models\c.alo"] = new("")
        });
        var spy = new SpyIndexService();

        var handler = BuildHandler(spy, fs: fs);
        await handler.Handle(Changed(
            "file:///c:/data/art/textures/a.tga",
            "file:///c:/data/art/textures/b.dds",
            "file:///c:/data/art/models/c.alo"), CancellationToken.None);

        Assert.Equal(1, spy.AssetApplications);
    }

    [Fact]
    public async Task Handle_MultipleDynamicEnumChanges_RescansEnumCatalogOnce()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\data\xml\gameconstants.xml"] =
                new("<GameConstants><Armor_Types>Armor_Structure</Armor_Types></GameConstants>"),
            [@"c:\mods\mymod\data\xml\surfacefxtriggertype.xml"] =
                new("<Enum><Entry>Dirt</Entry></Enum>")
        });
        var spy = new SpyIndexService();
        var schema = new FakeSchemaProvider(
            new EnumDefinition
            {
                Name = "ArmorType", Kind = EnumKind.DynamicXml,
                SourceFile = "data/xml/gameconstants.xml$Armor_Types", Values = []
            },
            new EnumDefinition
            {
                Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
                SourceFile = "data/xml/surfacefxtriggertype.xml", Values = []
            });
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration(["c:/mods/mymod/data/xml"], [], [], [], "csv")
        };

        var handler = BuildHandler(spy, fs: fs, reload: reload, schema: schema);
        await handler.Handle(Changed(
            "file:///c:/mods/mymod/data/xml/gameconstants.xml",
            "file:///c:/mods/mymod/data/xml/surfacefxtriggertype.xml"), CancellationToken.None);

        Assert.Single(spy.DynamicEnumApplications);
    }

    [Fact]
    public async Task Handle_MultiplePgprojChanges_ReloadsOnce()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\mymod.pgproj"] = new("{}"),
            [@"c:\mods\dep\dep.pgproj"] = new("{}")
        });
        var reload = new FakeReloadService();

        var handler = BuildHandler(fs: fs, reload: reload);
        await handler.Handle(Changed(
            "file:///c:/mods/mymod/mymod.pgproj",
            "file:///c:/mods/dep/dep.pgproj"), CancellationToken.None);

        Assert.Equal(1, reload.ReloadCount);
    }

    [Fact]
    public async Task Handle_PgprojAndAssetChanged_ReloadSubsumesAssetReglob()
    {
        // The full project reload re-runs every catalog itself — a separate asset re-glob in the
        // same notification would be redundant work on the old configuration.
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\mymod.pgproj"] = new("{}"),
            [@"c:\data\art\textures\a.tga"] = new("")
        });
        var spy = new SpyIndexService();
        var reload = new FakeReloadService();

        var handler = BuildHandler(spy, fs: fs, reload: reload);
        await handler.Handle(Changed(
            "file:///c:/mods/mymod/mymod.pgproj",
            "file:///c:/data/art/textures/a.tga"), CancellationToken.None);

        Assert.Equal(1, reload.ReloadCount);
        Assert.Equal(0, spy.AssetApplications);
    }

    [Fact]
    public async Task Handle_PgprojAndCsvChanged_ReloadSubsumesLocalisationReload()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\mymod.pgproj"] = new("{}"),
            [@"c:\mods\mymod\data\text\mastertextfile.csv"] = new("key,ENGLISH")
        });
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration([], [], ["c:/mods/mymod/data/text"], [], "csv")
        };

        var handler = BuildHandler(fs: fs, reload: reload);
        await handler.Handle(Changed(
            "file:///c:/mods/mymod/mymod.pgproj",
            "file:///c:/mods/mymod/data/text/mastertextfile.csv"), CancellationToken.None);

        Assert.Equal(1, reload.ReloadCount);
        Assert.Equal(0, reload.LocalisationReloadCount);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeReloadService : IModProjectReloadService
    {
        public int ReloadCount { get; private set; }
        public int LocalisationReloadCount { get; private set; }

        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig { get; init; }
        public IReadOnlyList<string>? LastWorkspaceRoots => null;

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken ct)
        {
            ReloadCount++;
            return Task.CompletedTask;
        }

        public Task ReloadLocalisationAsync(CancellationToken ct)
        {
            LocalisationReloadCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSchemaProvider(params EnumDefinition[] enums) : ISchemaProvider
    {
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => enums;
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];

        public event EventHandler? SchemaRefreshed
        {
            add { value?.Invoke(this, EventArgs.Empty); }
            remove { }
        }

        public XmlTagDefinition? GetTag(string tagName)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string enumName)
        {
            return enums.FirstOrDefault(e => e.Name.Equals(enumName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class NullLocalisationLoader : ILocalisationLoader
    {
        public Task LoadAsync(WorkspaceConfiguration workspaceConfig, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SpyIndexService : IGameIndexService
    {
        public readonly List<string> Removals = [];

        public readonly List<(string Uri, string Text)> Updates = [];
        public GameIndex Current => GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;
        public event Action<ILocalisationIndex>? LocalisationChanged;
        public event Action<GameIndex>? DynamicEnumChanged;

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            Updates.Add((uri, text));
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
            Removals.Add(uri);
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public int AssetApplications { get; private set; }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
            AssetApplications++;
        }

        public void ApplyModelBones(
            ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }

        public readonly List<ImmutableDictionary<string, ImmutableArray<string>>> DynamicEnumApplications = [];

        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
        {
            DynamicEnumApplications.Add(values);
        }
        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return NullDisposable.Instance;
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
        }

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }

        public IEnumerable<TrackedDocument> All => _docs.Values;
    }
}
