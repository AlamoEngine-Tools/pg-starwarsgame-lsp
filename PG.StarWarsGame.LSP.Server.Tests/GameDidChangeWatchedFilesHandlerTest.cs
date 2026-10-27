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
using PG.StarWarsGame.LSP.Lua.Schema;
using PG.StarWarsGame.LSP.Server.Assets;
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
        FakeGameIndexService? index = null,
        FakeGameWorkspaceHost? host = null,
        MockFileSystem? fs = null,
        FakeReloadService? reload = null,
        ISchemaProvider? schema = null,
        ILocalisationWriteLedger? writeLedger = null)
    {
        var fileSystem = fs ?? new MockFileSystem();
        var fileHelper = new FileHelper(fileSystem);
        var idx = index ?? new FakeGameIndexService();
        var schemaProvider = schema ?? new SchemaProviderBuilder().Build();
        var indexer = new WorkspaceIndexer(
            fileHelper,
            [],
            idx,
            new FileTypeRegistry(),
            schemaProvider,
            new EaWXmlContext(fileHelper),
            new NullProjectIndexCache(),
            new StoryChainProblemStore(),
            new FakeLspConfigurationProvider(),
            NoMegEntries.Instance,
            new MegArchiveLoadOrder(fileHelper),
            new NullModelBoneCatalogCache(),
            NullLogger<WorkspaceIndexer>.Instance, new LuaApiSchemaProvider([]));
        return new GameDidChangeWatchedFilesHandler(
            idx,
            host ?? new FakeGameWorkspaceHost(),
            fileHelper,
            indexer,
            reload ?? new FakeReloadService(),
            schemaProvider,
            writeLedger ?? new LocalisationWriteLedger(fileHelper),
            new OpenStartupGate(),
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
        var spy = new FakeGameIndexService();

        var handler = BuildHandler(spy, fs: fs);
        await handler.Handle(Changed(XmlUri), CancellationToken.None);

        Assert.Contains(spy.Updates, u => u is { Uri: "file:///c:/data/units.xml", Text: freshContent });
    }

    [Fact]
    public async Task Handle_ChangedFile_OpenInEditor_DoesNotReIndex()
    {
        const string path = @"c:\scripts\mission.lua";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new("-- stale disk content")
        });
        var host = new FakeGameWorkspaceHost();
        host.AddOrUpdate(LuaUri, "-- live editor content", 5);
        var spy = new FakeGameIndexService();

        var handler = BuildHandler(spy, host, fs);
        await handler.Handle(Changed(LuaUri), CancellationToken.None);

        Assert.Empty(spy.Updates);
    }

    // ── deleted file ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_DeletedFile_RemovesFromIndex()
    {
        var spy = new FakeGameIndexService();

        var handler = BuildHandler(spy);
        await handler.Handle(Deleted(XmlUri), CancellationToken.None);

        Assert.Contains(spy.Removals, r => r == "file:///c:/data/units.xml");
        Assert.Empty(spy.Updates);
    }

    // ── file not found on disk ────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ChangedFile_NotOnDisk_IsIgnored()
    {
        var spy = new FakeGameIndexService();

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
        var spy = new FakeGameIndexService();

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
        var spy = new FakeGameIndexService();
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
        var spy = new FakeGameIndexService();
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
        var spy = new FakeGameIndexService();

        var handler = BuildHandler(spy, fs: fs);
        await handler.Handle(Changed(assetUri), CancellationToken.None);

        Assert.Empty(spy.Updates);
    }

    /// <summary>
    ///     A script is both a document and a catalog entry: an edit re-indexes it, and the catalog
    ///     a <c>Lua_Script</c> resolves against is re-globbed.
    /// </summary>
    [Fact]
    public async Task Handle_ScriptChanged_ReIndexesAndReglobsTheCatalog()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\scripts\mission.lua"] = new("-- script")
        });
        var spy = new FakeGameIndexService();

        var handler = BuildHandler(spy, fs: fs);
        await handler.Handle(Changed(LuaUri), CancellationToken.None);

        Assert.Single(spy.Updates);
        Assert.Equal(1, spy.AssetApplications);
    }

    /// <summary>
    ///     The catalog answers whether a file exists, so a deleted one has to leave it - or a
    ///     reference to it keeps resolving.
    /// </summary>
    [Theory]
    [InlineData(LuaUri)]
    [InlineData("file:///c:/data/art/textures/foo.tga")]
    public async Task Handle_AssetDeleted_ReglobsTheCatalog(string uri)
    {
        var spy = new FakeGameIndexService();

        var handler = BuildHandler(spy, fs: new MockFileSystem());
        await handler.Handle(Deleted(uri), CancellationToken.None);

        Assert.Equal(1, spy.AssetApplications);
    }

    [Fact]
    public async Task Handle_ScriptDeleted_RemovesItFromTheIndex()
    {
        var spy = new FakeGameIndexService();

        var handler = BuildHandler(spy, fs: new MockFileSystem());
        await handler.Handle(Deleted(LuaUri), CancellationToken.None);

        Assert.Contains(spy.Removals, r => r == LuaUri);
    }

    // ── watched globs ────────────────────────────────────────────────────────────

    /// <summary>
    ///     Every file kind the server reacts to on disk has to be watched, or that reaction is
    ///     simply dead: the handler can only run for a notification the client was asked to send.
    /// </summary>
    /// <remarks>
    ///     Inherited from a test on the old <c>ProjectFileWatcherRegistrar</c>, which built a second
    ///     copy of this list that nothing used - and had already drifted, missing <c>**/*.dat</c>.
    ///     That class is gone; this asserts the list that is actually registered.
    /// </remarks>
    [Theory]
    [InlineData("**/*.xml")] // game data AND dynamic enum sources
    [InlineData("**/*.lua")] // story scripts
    [InlineData("**/*.pgproj")] // project file - a change re-derives the whole configuration
    [InlineData("**/*.csv")] // localisation
    [InlineData("**/*.properties")]
    [InlineData("**/*.dat")] // the engine's own localisation format
    public void WatchedGlobs_CoversEveryFileKindTheServerReactsTo(string glob)
    {
        Assert.Contains(glob, GameDidChangeWatchedFilesHandler.WatchedGlobs);
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
        var spy = new FakeGameIndexService();
        var schema = new SchemaProviderBuilder().WithEnums(new EnumDefinition
        {
            Name = "ArmorType", Kind = EnumKind.DynamicXml,
            SourceFile = "data/xml/gameconstants.xml$Armor_Types", Values = []
        }).Build();
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration(["c:/mods/mymod/data/xml"], [], [], [], "csv")
        };

        var handler = BuildHandler(spy, fs: fs, reload: reload, schema: schema);
        await handler.Handle(Changed(xmlUri), CancellationToken.None);

        var applied = Assert.Single(spy.DynamicEnumApplications);
        Assert.Contains("ArmourG_Structure", applied["ArmorType"]);
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
        var spy = new FakeGameIndexService();
        var schema = new SchemaProviderBuilder().WithEnums(new EnumDefinition
        {
            Name = "ArmorType", Kind = EnumKind.DynamicXml,
            SourceFile = "data/xml/gameconstants.xml$Armor_Types", Values = []
        }).Build();

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
        var spy = new FakeGameIndexService();
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration([], [], ["c:/mods/mymod/data/text"], [], "csv")
        };

        var handler = BuildHandler(spy, fs: fs, reload: reload);
        await handler.Handle(Changed(csvUri), CancellationToken.None);

        Assert.Equal(1, reload.LocalisationReloadCount);
        Assert.Empty(spy.Updates);
    }

    // The storm this closes: saving from the localisation editor already reloads the index, and the
    // watcher then reports that same write - so one save reloaded twice and told every open tab the
    // index had moved twice, each time costing a full re-read of a 19,000-row file.
    [Fact]
    public async Task Handle_CsvUnderTextRoot_HoldingWhatTheServerJustWrote_DoesNotReloadAgain()
    {
        const string csvUri = "file:///c:/mods/mymod/data/text/mastertextfile.csv";
        const string path = @"c:\mods\mymod\data\text\mastertextfile.csv";
        const string content = "key,ENGLISH\r\nTEXT_A,Hello\r\n";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [path] = new(content) });
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration([], [], ["c:/mods/mymod/data/text"], [], "csv")
        };
        var ledger = new LocalisationWriteLedger(new FileHelper(fs));
        ledger.Record(path, LocalisationContentHash.Compute(content));

        var handler = BuildHandler(new FakeGameIndexService(), fs: fs, reload: reload, writeLedger: ledger);
        await handler.Handle(Changed(csvUri), CancellationToken.None);

        Assert.Equal(0, reload.LocalisationReloadCount);
    }

    // Matching on content is what makes the suppression safe - an edit that landed between the
    // server's write and the watcher event still has to be picked up.
    [Fact]
    public async Task Handle_CsvUnderTextRoot_ChangedSinceTheServerWroteIt_StillReloads()
    {
        const string csvUri = "file:///c:/mods/mymod/data/text/mastertextfile.csv";
        const string path = @"c:\mods\mymod\data\text\mastertextfile.csv";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new("key,ENGLISH\r\nTEXT_A,Edited by someone else\r\n")
        });
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration([], [], ["c:/mods/mymod/data/text"], [], "csv")
        };
        var ledger = new LocalisationWriteLedger(new FileHelper(fs));
        ledger.Record(path, LocalisationContentHash.Compute("key,ENGLISH\r\nTEXT_A,Hello\r\n"));

        var handler = BuildHandler(new FakeGameIndexService(), fs: fs, reload: reload, writeLedger: ledger);
        await handler.Handle(Changed(csvUri), CancellationToken.None);

        Assert.Equal(1, reload.LocalisationReloadCount);
    }


    [Fact]
    public async Task Handle_PropertiesUnderTextRoot_TriggersLocalisationReload()
    {
        const string propsUri = "file:///c:/mods/mymod/data/text/mastertextfile.properties";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\data\text\mastertextfile.properties"] = new("TEXT_A=Hello")
        });
        var spy = new FakeGameIndexService();
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
        // A localisation XML file matches the blanket **/*.xml watcher too - it must be routed to
        // the localisation reload, not the game-XML document parser (no parser understands it).
        const string xmlUri = "file:///c:/mods/mymod/data/text/mastertextfile.xml";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\data\text\mastertextfile.xml"] = new("<LocalisationData/>")
        });
        var spy = new FakeGameIndexService();
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
        var spy = new FakeGameIndexService();
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
        var spy = new FakeGameIndexService();
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
        var spy = new FakeGameIndexService();
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration([], [], ["c:/mods/mymod/data/text"], [], "csv")
        };

        var handler = BuildHandler(spy, fs: fs, reload: reload);
        await handler.Handle(Changed(csvUri), CancellationToken.None);

        Assert.Equal(0, reload.LocalisationReloadCount);
    }

    /// <summary>
    ///     A file in a subfolder of a text root is not the loader's business, so it must not force a
    ///     rebuild.
    ///     <para>
    ///         The text-root test was a recursive prefix match while
    ///         <c>LocalisationLoader.EnumerateFromTextRoots</c> globs <c>TopDirectoryOnly</c>, so a
    ///         change below the root paid for a full localisation reload that could not load the file
    ///         that triggered it.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task Handle_CsvInASubfolderOfATextRoot_NotRoutedToLocalisationReload()
    {
        const string csvUri = "file:///c:/mods/mymod/data/text/archive/old.csv";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\data\text\archive\old.csv"] = new("key,ENGLISH")
        });
        var spy = new FakeGameIndexService();
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration([], [], ["c:/mods/mymod/data/text"], [], "csv")
        };

        var handler = BuildHandler(spy, fs: fs, reload: reload);
        await handler.Handle(Changed(csvUri), CancellationToken.None);

        Assert.Equal(0, reload.LocalisationReloadCount);
    }

    /// <summary>
    ///     DAT is a localisation format a project can declare, and the engine's own files are DAT -
    ///     it went unwatched entirely, so the one format that is genuinely split per language never
    ///     noticed a change on disk.
    /// </summary>
    [Fact]
    public async Task Handle_DatInATextRoot_IsRoutedToLocalisationReload()
    {
        const string datUri = "file:///c:/mods/mymod/data/text/mastertextfile_english.dat";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\data\text\mastertextfile_english.dat"] = new([1, 2, 3])
        });
        var spy = new FakeGameIndexService();
        var reload = new FakeReloadService
        {
            LastWorkspaceConfig = new WorkspaceConfiguration([], [], ["c:/mods/mymod/data/text"], [], "dat")
        };

        var handler = BuildHandler(spy, fs: fs, reload: reload);
        await handler.Handle(Changed(datUri), CancellationToken.None);

        Assert.Equal(1, reload.LocalisationReloadCount);
    }

    [Fact]
    public async Task Handle_CsvChanged_NoWorkspaceConfigYet_NotRoutedToLocalisationReload()
    {
        // LastWorkspaceConfig is null before the first successful LoadAsync - the handler must not
        // throw, and the change is simply ignored as an unrecognised document.
        const string csvUri = "file:///c:/mods/mymod/data/text/mastertextfile.csv";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\data\text\mastertextfile.csv"] = new("key,ENGLISH")
        });
        var spy = new FakeGameIndexService();
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
        var spy = new FakeGameIndexService();

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
        var spy = new FakeGameIndexService();
        var schema = new SchemaProviderBuilder().WithEnums(
            new EnumDefinition
            {
                Name = "ArmorType", Kind = EnumKind.DynamicXml,
                SourceFile = "data/xml/gameconstants.xml$Armor_Types", Values = []
            },
            new EnumDefinition
            {
                Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
                SourceFile = "data/xml/surfacefxtriggertype.xml", Values = []
            }).Build();
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
        // The full project reload re-runs every catalog itself - a separate asset re-glob in the
        // same notification would be redundant work on the old configuration.
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\mods\mymod\mymod.pgproj"] = new("{}"),
            [@"c:\data\art\textures\a.tga"] = new("")
        });
        var spy = new FakeGameIndexService();
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

    /// <summary>
    ///     A gate that is already open, so a change is handled on the spot. The buffering path is the
    ///     startup pipeline's own concern and is covered by <c>StartupGateTest</c>.
    /// </summary>
    private sealed class OpenStartupGate : IStartupGate
    {
        public bool IsOpen => true;

        public event Action? Opened
        {
            add { }
            remove { }
        }

        public Task RunOrBufferAsync(Func<CancellationToken, Task> action, CancellationToken ct)
        {
            return action(ct);
        }

        public Task OpenAsync()
        {
            return Task.CompletedTask;
        }
    }
}