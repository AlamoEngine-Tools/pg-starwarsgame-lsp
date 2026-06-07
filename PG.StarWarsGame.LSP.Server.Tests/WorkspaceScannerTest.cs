// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class WorkspaceScannerTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    // Builds a scanner that pre-registers the given roots as EaW XML directories, so all
    // files inside them are treated as EaW XML files. Used by tests that only care about
    // basic indexing mechanics, not directory-detection logic.
    private static WorkspaceScanner Build(MockFileSystem fs, FakeIndexService svc,
        IEnumerable<string> eawRoots, params IGameDocumentParser[] parsers)
    {
        return Build(fs, svc, new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance), eawRoots, parsers);
    }

    private static WorkspaceScanner Build(MockFileSystem fs, FakeIndexService svc,
        IGameWorkspaceHost host, IEnumerable<string> eawRoots, params IGameDocumentParser[] parsers)
    {
        var fh = new FileHelper(fs);
        var ctx = new EaWXmlContext(fh);
        foreach (var r in eawRoots)
            ctx.AddDirectory(r);
        return new WorkspaceScanner(fh, parsers, svc, host,
            NullLogger<WorkspaceScanner>.Instance, null,
            new FileTypeRegistry(), new FakeSchemaProvider(), ctx, new PreOpenBuffer(),
            new NullLocalisationLoader());
    }

    private static WorkspaceScanner BuildWithBuffer(MockFileSystem fs, FakeIndexService svc,
        IGameWorkspaceHost host, IEnumerable<string> eawRoots, IPreOpenBuffer buffer,
        params IGameDocumentParser[] parsers)
    {
        var fh = new FileHelper(fs);
        var ctx = new EaWXmlContext(fh);
        foreach (var r in eawRoots)
            ctx.AddDirectory(r);
        return new WorkspaceScanner(fh, parsers, svc, host,
            NullLogger<WorkspaceScanner>.Instance, null,
            new FileTypeRegistry(), new FakeSchemaProvider(), ctx, buffer,
            new NullLocalisationLoader());
    }

    private static WorkspaceScanner Build(MockFileSystem fs, FakeIndexService svc,
        IFileTypeRegistry registry, ISchemaProvider schema,
        params IGameDocumentParser[] parsers)
    {
        return Build(fs, svc, new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance), registry, schema, parsers);
    }

    private static WorkspaceScanner Build(MockFileSystem fs, FakeIndexService svc,
        IGameWorkspaceHost host, IFileTypeRegistry registry, ISchemaProvider schema,
        params IGameDocumentParser[] parsers)
    {
        var fh = new FileHelper(fs);
        return new WorkspaceScanner(fh, parsers, svc, host,
            NullLogger<WorkspaceScanner>.Instance, null,
            registry, schema, new EaWXmlContext(fh), new PreOpenBuffer(),
            new NullLocalisationLoader());
    }

    private static (WorkspaceScanner Scanner, EaWXmlContext Context) BuildWithContext(
        MockFileSystem fs, FakeIndexService svc, IFileTypeRegistry registry, ISchemaProvider schema,
        params IGameDocumentParser[] parsers)
    {
        var fh = new FileHelper(fs);
        var ctx = new EaWXmlContext(fh);
        var scanner = new WorkspaceScanner(fh, parsers, svc,
            new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance),
            NullLogger<WorkspaceScanner>.Instance, null,
            registry, schema, ctx, new PreOpenBuffer(),
            new NullLocalisationLoader());
        return (scanner, ctx);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_GlobsAssetFiles_AppliesMergedCatalog()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>"),
            [Path.Combine(root, "Data", "Art", "Textures", "Foo.tga")] = new(""),
            [Path.Combine(root, "Data", "Art", "Models", "Bar.alo")] = new(""),
            [Path.Combine(root, "readme.txt")] = new("")
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, [root], new FakeParser()).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.NotNull(svc.AppliedAssetFiles);
        Assert.True(svc.AppliedAssetFiles!.Contains("data/art/textures/foo.tga"));
        Assert.True(svc.AppliedAssetFiles.Contains("data/art/models/bar.alo"));
        Assert.False(svc.AppliedAssetFiles.Contains("readme.txt"));
    }

    [Fact]
    public async Task ScanAsync_AppliesModelBones_FromBaseline()
    {
        // No workspace .alo files; the scanner must still publish the baseline bones via ApplyModelBones.
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>")
        });
        var baseline = BaselineIndex.Empty with
        {
            ModelBones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("data/art/models/shipped.alo", ["root", "muzzle_bone"])
        };
        var svc = new FakeIndexService(GameIndex.Empty with { Baseline = baseline });

        await Build(fs, svc, [root], new FakeParser()).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.NotNull(svc.AppliedModelBones);
        Assert.Equal(["root", "muzzle_bone"], svc.AppliedModelBones!["data/art/models/shipped.alo"].ToArray());
    }

    [Fact]
    public async Task ScanAsync_MergesBaselineAssetFiles()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "Data", "Art", "Textures", "Local.tga")] = new("")
        });
        var baseline = BaselineIndex.Empty with
        {
            AssetFiles = ImmutableHashSet.Create("data/art/textures/shipped.tga")
        };
        var svc = new FakeIndexService(GameIndex.Empty with { Baseline = baseline });

        await Build(fs, svc, [root], new FakeParser()).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.NotNull(svc.AppliedAssetFiles);
        Assert.True(svc.AppliedAssetFiles!.Contains("data/art/textures/local.tga"));
        Assert.True(svc.AppliedAssetFiles.Contains("data/art/textures/shipped.tga"));
    }

    [Fact]
    public async Task ScanAsync_IndexedUri_HasFileScheme()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, [root], new FakeParser()).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.StartsWith("file://", svc.Calls[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_IndexedUri_IsCanonicalLowercase()
    {
        // Scanner must produce canonical file:/// URIs (lowercase, forward-slash) so that
        // index lookups and file-type registry lookups always use the same key format as
        // the LSP client URIs after normalization.
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "Units.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, [root], new FakeParser()).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        var uri = svc.Calls[0].Uri;
        Assert.StartsWith("file:///", uri, StringComparison.Ordinal);
        Assert.Equal(uri, uri.ToLowerInvariant());
        Assert.DoesNotContain('\\', uri);
    }

    [Fact]
    public async Task ScanAsync_ParseableFiles_AreIndexedWithVersionZero()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>"),
            [Path.Combine(root, "b.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, [root], new FakeParser()).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.Equal(2, svc.Calls.Count);
        Assert.All(svc.Calls, c => Assert.Equal(0, c.Version));
    }

    [Fact]
    public async Task ScanAsync_UnparseableExtension_Skipped()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>"),
            [Path.Combine(root, "b.lua")] = new("-- lua")
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, [root], new FakeParser()).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.Single(svc.Calls);
        Assert.EndsWith(".xml", svc.Calls[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_MultipleWorkspaceFolders_AllFilesIndexed()
    {
        var root1 = Root("ws1");
        var root2 = Root("ws2");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root1, "a.xml")] = new("<Root/>"),
            [Path.Combine(root2, "b.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, [root1, root2], new FakeParser()).ScanAsync(WorkspaceConfiguration.Empty, [root1, root2], CancellationToken.None);

        Assert.Equal(2, svc.Calls.Count);
    }

    [Fact]
    public async Task ScanAsync_PreCancelledToken_ThrowsAndDoesNotIndex()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Build(fs, svc, [root], new FakeParser()).ScanAsync(WorkspaceConfiguration.Empty, [root], cts.Token));

        Assert.Empty(svc.Calls);
    }

    [Fact]
    public async Task ScanAsync_UsesBeginBulkUpdate_Exactly_Once()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>"),
            [Path.Combine(root, "b.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, [root], new FakeParser()).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.Equal(1, svc.BeginBulkUpdateCallCount);
    }

    [Fact]
    public async Task ScanAsync_EmptyFolder_IndexesNothing()
    {
        var root = Root("ws");
        var fs = new MockFileSystem();
        fs.AddDirectory(root);
        var svc = new FakeIndexService();

        await Build(fs, svc, [root], new FakeParser()).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.Empty(svc.Calls);
    }
    // ── PreScanMetafiles ─────────────────────────────────────────────────────

    [Fact]
    public async Task PreScan_FileRegistry_RegistersFilesFromMetafile()
    {
        var root = Root("ws");
        // Lowercase path — MockFileSystem uses case-sensitive keys; FindInWorkspace
        // searches with the normalised (lowercase) relative path from MetafileDefinition.
        var metafilePath = Path.Combine(root, "data", "xml", "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new("<Game_Object_Files><File>DATA\\XML\\HARDPOINTS.XML</File></Game_Object_Files>")
        });
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var svc = new FakeIndexService();

        await Build(fs, svc, registry, schema).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        var expectedKey = new FileHelper(fs).PathToFileUri(
            Path.Combine(root, "data", "xml", "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public async Task PreScan_FileRegistry_FallsBackToBaseline_WhenMetafileAbsent()
    {
        var root = Root("ws");
        var fs = new MockFileSystem();
        fs.AddDirectory(root);
        var fileTypeMap = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("data/xml/hardpoints.xml", ImmutableArray.Create("GameObjectType"));
        var baseline = new BaselineIndex(
            ImmutableDictionary<string, GameSymbol>.Empty,
            DateTimeOffset.UtcNow, "",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            fileTypeMap);
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var svc = new FakeIndexService(new GameIndex(baseline,
            ImmutableDictionary<string, DocumentIndex>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty));

        await Build(fs, svc, registry, schema).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        var expectedKey = new FileHelper(fs).PathToFileUri(
            Path.Combine(root, "data", "xml", "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public async Task PreScan_FileRegistry_FallsBackToBaseline_UsesXmlDirs_WhenPgprojPresent()
    {
        // When pgproj supplies XML dirs that differ from the standard game layout (e.g. "xml/"
        // instead of "data/xml/"), FallbackFromBaseline must register file types using those
        // dirs with filename-only (not workspace-root + full baseline path, which would double
        // the "data/xml/" prefix and produce a wrong path).
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "xml"); // non-standard: "xml/" not "data/xml/"
        var fs = new MockFileSystem();
        fs.AddDirectory(xmlDir);
        var fileTypeMap = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("data/xml/hardpoints.xml", ImmutableArray.Create("GameObjectType"));
        var baseline = new BaselineIndex(
            ImmutableDictionary<string, GameSymbol>.Empty,
            DateTimeOffset.UtcNow, "",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            fileTypeMap);
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry,
                ["GameObjectType"]));
        var svc = new FakeIndexService(new GameIndex(baseline,
            ImmutableDictionary<string, DocumentIndex>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty));
        var (scanner, _) = BuildWithContext(fs, svc, registry, schema);

        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        await scanner.ScanAsync(config, [root], CancellationToken.None);

        // Type must be registered at "xmlDir/hardpoints.xml" (filename-only, not root/data/xml/hardpoints.xml).
        var expectedKey = new FileHelper(fs).PathToFileUri(Path.Combine(xmlDir, "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public async Task PreScan_DirectContent_RegistersMetafilePath()
    {
        var root = Root("ws");
        var moviesPath = Path.Combine(root, "DATA", "XML", "MOVIES.XML");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [moviesPath] = new("<Movies/>")
        });
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/movies.xml", MetafileType.DirectContent, ["BinkMovie"]));
        var svc = new FakeIndexService();

        await Build(fs, svc, registry, schema).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        var expectedKey = new FileHelper(fs).PathToFileUri(
            Path.Combine(root, "data", "xml", "movies.xml"));
        Assert.Equal(["BinkMovie"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public async Task PreScan_Special_IsSkipped()
    {
        var root = Root("ws");
        var campaignPath = Path.Combine(root, "DATA", "XML", "CAMPAIGNS.XML");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [campaignPath] = new("<Campaigns/>")
        });
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/campaigns.xml", MetafileType.Special, ["StoryParser"]));
        var svc = new FakeIndexService();

        await Build(fs, svc, registry, schema).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.Empty(registry.All);
    }

    [Fact]
    public async Task PreScan_FileRegistry_WorkspaceIsXmlDirectory_RegistersFilesFromMetafile()
    {
        // Workspace root IS the XML directory: metafile lives at root/<name>.xml
        // (no data/xml/ prefix in the key). Files inside the metafile are listed
        // as DATA\XML\FOO.XML — they resolve to root/<foo>.xml.
        var root = Root("ws");
        var metafilePath = Path.Combine(root, "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new("<Game_Object_Files><File>DATA\\XML\\HARDPOINTS.XML</File></Game_Object_Files>")
        });
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var svc = new FakeIndexService();

        await Build(fs, svc, registry, schema).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        var expectedKey = new FileHelper(fs).PathToFileUri(
            Path.Combine(root, "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    // ── EaWXmlContext population ─────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_RegistersMetafileDirectory_InContext()
    {
        var root = Root("ws");
        var metafilePath = Path.Combine(root, "data", "xml", "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new("<Files/>")
        });
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var (scanner, ctx) = BuildWithContext(fs, new FakeIndexService(), new FileTypeRegistry(), schema);

        await scanner.ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        var xmlFileUri = new FileHelper(fs).PathToFileUri(
            Path.Combine(root, "data", "xml", "Units.xml"));
        Assert.True(ctx.IsEaWXmlFile(xmlFileUri));
    }

    [Fact]
    public async Task ScanAsync_FilesOutsideEaWDirectory_NotRegisteredInContext()
    {
        var root = Root("ws");
        var metafilePath = Path.Combine(root, "data", "xml", "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new("<Files/>")
        });
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var (scanner, ctx) = BuildWithContext(fs, new FakeIndexService(), new FileTypeRegistry(), schema);

        await scanner.ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        var nonEaWUri = new FileHelper(fs).PathToFileUri(Path.Combine(root, "scripts", "build.xml"));
        Assert.False(ctx.IsEaWXmlFile(nonEaWUri));
    }

    [Fact]
    public async Task ScanAsync_OnlyIndexesFilesInEaWDirectory()
    {
        // Files in data/xml/ (the detected EaW XML dir) are indexed; scripts/build.xml is not.
        var root = Root("ws");
        var metafilePath = Path.Combine(root, "data", "xml", "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new("<Files/>"),
            [Path.Combine(root, "data", "xml", "Units.xml")] = new("<Root/>"),
            [Path.Combine(root, "scripts", "build.xml")] = new("<project/>")
        });
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var svc = new FakeIndexService();
        var (scanner, _) = BuildWithContext(fs, svc, new FileTypeRegistry(), schema, new FakeParser());

        await scanner.ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        // Both xml files inside the EaW directory are indexed; build.xml (outside) is not.
        Assert.Equal(2, svc.Calls.Count);
        Assert.DoesNotContain(svc.Calls, c => c.Uri.EndsWith("build.xml", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(svc.Calls, c => c.Uri.EndsWith("units.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanAsync_LuaFiles_IndexedRegardlessOfEaWXmlContext()
    {
        // Lua files live in Scripts/, which is never registered in EaWXmlContext.
        // They must be indexed without the XML directory gate.
        var root = Root("ws");
        var metafilePath = Path.Combine(root, "data", "xml", "gameobjectfiles.xml");
        var luaPath = Path.Combine(root, "data", "scripts", "story.lua");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new("<Files/>"),
            [luaPath] = new("function Definitions() end")
        });
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var svc = new FakeIndexService();
        var (scanner, _) = BuildWithContext(fs, svc, new FileTypeRegistry(), schema,
            new FakeParser(), new FakeParser(".lua"));

        await scanner.ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.Contains(svc.Calls, c => c.Uri.EndsWith("story.lua", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanAsync_NoMetafilesFound_HeuristicFallback_IndexesFilesInRoot()
    {
        // No metafile found on disk → heuristic fallback seeds context with workspace roots
        // → bulk scan indexes XML files found in those roots.
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "data", "xml", "Units.xml")] = new("<Root/>")
        });
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var svc = new FakeIndexService();
        var (scanner, _) = BuildWithContext(fs, svc, new FileTypeRegistry(), schema, new FakeParser());

        await scanner.ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.NotEmpty(svc.Calls);
        Assert.Contains(svc.Calls, c => c.Uri.EndsWith("units.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanAsync_HeuristicMode_NoMetafiles_SeedsContextWithWorkspaceRoots()
    {
        // When no pgproj and no metafiles: heuristic fallback seeds EaWXmlContext
        // with the workspace roots so IsEaWXmlFile is true for any file under them.
        var root = Root("ws");
        var fs = new MockFileSystem();
        fs.AddDirectory(root);
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var (scanner, ctx) = BuildWithContext(fs, new FakeIndexService(), new FileTypeRegistry(), schema);

        await scanner.ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        var anyFileUri = new FileHelper(fs).PathToFileUri(Path.Combine(root, "data", "xml", "units.xml"));
        Assert.True(ctx.IsEaWXmlFile(anyFileUri));
    }

    [Fact]
    public async Task ScanAsync_WaitsForSchema_WhenAllMetafilesEmptyAtStart()
    {
        // Simulate HttpSchemaProvider: schema is empty at scan start and fires
        // SchemaRefreshed after the background HTTP download completes.
        var root = Root("ws");
        var metafilePath = Path.Combine(root, "data", "xml", "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new("<Files><File>DATA\\XML\\HARDPOINTS.XML</File></Files>")
        });
        var registry = new FileTypeRegistry();
        var schema = new DelayedSchemaProvider();
        var svc = new FakeIndexService();
        var scanner = Build(fs, svc, registry, schema);

        // ScanAsync will yield at WaitForSchemaAsync since AllMetafiles is empty.
        var scanTask = scanner.ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        // Simulate schema load completing and firing SchemaRefreshed.
        schema.LoadMetafiles(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry,
                ["GameObjectType"]));

        await scanTask;

        var expectedKey = new FileHelper(fs).PathToFileUri(
            Path.Combine(root, "data", "xml", "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    // ── SchemaRefreshed after scan ───────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_PgprojDirectories_SetInContextBeforeSchemaLoads()
    {
        // pgproj supplies explicit XmlDirectories → SetDirectories must be called BEFORE
        // WaitForSchemaAsync, so IsEaWXmlFile returns true even while schema is loading.
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem();
        fs.AddDirectory(xmlDir);
        var fh = new FileHelper(fs);
        var ctx = new EaWXmlContext(fh);
        var schema = new BlockingSchemaProvider();
        var scanner = new WorkspaceScanner(fh, [], new FakeIndexService(),
            new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance),
            NullLogger<WorkspaceScanner>.Instance, null,
            new FileTypeRegistry(), schema, ctx, new PreOpenBuffer(),
            new NullLocalisationLoader());
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var scanTask = scanner.ScanAsync(config, [root], cts.Token);
        await schema.WhenWaiting.WaitAsync(TimeSpan.FromSeconds(2));

        var xmlFileUri = fh.PathToFileUri(Path.Combine(xmlDir, "units.xml"));
        Assert.True(ctx.IsEaWXmlFile(xmlFileUri));

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanTask);
    }

    [Fact]
    public async Task SchemaRefreshed_AfterScanCompletes_UpdatesEaWXmlContext()
    {
        // Scenario: schema loads AFTER the initial scan has already finished
        // (e.g. HTTP fetch completed after the 30-second WaitForSchemaAsync timeout).
        // After heuristic fallback seeds root, the schema refresh synchronously
        // adds the metafile-specific dir; IsEaWXmlFile stays true throughout.
        var root = Root("ws");
        var metafilePath = Path.Combine(root, "data", "xml", "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new("<Files/>")
        });
        var schema = new RefreshableSchemaProvider();
        var svc = new FakeIndexService();
        var (scanner, ctx) = BuildWithContext(fs, svc, new FileTypeRegistry(), schema);

        await scanner.ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        var xmlFileUri = new FileHelper(fs).PathToFileUri(Path.Combine(root, "data", "xml", "Units.xml"));
        // Heuristic fallback seeds root → file is already recognised before refresh.
        Assert.True(ctx.IsEaWXmlFile(xmlFileUri));

        schema.Refresh(new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry,
            ["GameObjectType"]));

        Assert.True(ctx.IsEaWXmlFile(xmlFileUri));
    }

    [Fact]
    public async Task SchemaRefreshed_AfterScanCompletes_ReindexesXmlFiles()
    {
        // SchemaRefreshed triggers a background re-scan. After heuristic fallback already
        // indexed files in the initial pass, the re-scan must index them again (so the
        // schema-enriched parse results replace the schema-less ones).
        var root = Root("ws");
        var metafilePath = Path.Combine(root, "data", "xml", "gameobjectfiles.xml");
        var xmlFilePath = Path.Combine(root, "data", "xml", "Units.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new("<Files/>"),
            [xmlFilePath] = new("<Root/>")
        });
        var schema = new RefreshableSchemaProvider();
        var svc = new FakeIndexService();
        var (scanner, _) = BuildWithContext(fs, svc, new FileTypeRegistry(), schema, new FakeParser());

        await scanner.ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);
        // Heuristic fallback already indexed files during the initial scan.
        var callsAfterInitial = svc.Calls.Count;
        Assert.True(callsAfterInitial > 0);

        var waitFor = svc.WhenCallCountReaches(callsAfterInitial + 1);
        schema.Refresh(new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry,
            ["GameObjectType"]));
        await waitFor.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(svc.Calls, c => c.Uri.EndsWith("units.xml", StringComparison.OrdinalIgnoreCase));
    }

    // ── PreOpenBuffer drain ───────────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_PreOpenBuffer_FilesAddedToWorkspaceHost()
    {
        // Buffer contains a file VS Code had open before EaWXmlContext was configured.
        // After scan configures the context, the scanner drains the buffer and adds it.
        var root = Root("ws");
        var uri = new FileHelper(new MockFileSystem()).PathToFileUri(Path.Combine(root, "units.xml"));
        var fs = new MockFileSystem();
        fs.AddDirectory(root);
        var host = new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance);
        var buffer = new FakePreOpenBuffer((uri, "<EditorText/>", 1));
        var svc = new FakeIndexService();

        await BuildWithBuffer(fs, svc, host, [root], buffer).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.True(host.TryGet(uri, out var doc));
        Assert.Equal("<EditorText/>", doc.Text);
        Assert.Equal(1, doc.Version);
    }

    [Fact]
    public async Task ScanAsync_PreOpenBuffer_NonEaWFilesSkipped()
    {
        // Buffer contains a non-EaW file (e.g. a Lua script). It must not be seeded.
        var root = Root("ws");
        var nonEaWUri = new FileHelper(new MockFileSystem())
            .PathToFileUri(Path.Combine(Root("other"), "script.lua"));
        var fs = new MockFileSystem();
        fs.AddDirectory(root);
        var host = new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance);
        var buffer = new FakePreOpenBuffer((nonEaWUri, "-- lua", 1));
        var svc = new FakeIndexService();

        await BuildWithBuffer(fs, svc, host, [root], buffer).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.False(host.TryGet(nonEaWUri, out _));
    }

    [Fact]
    public async Task ScanAsync_PreOpenBuffer_DoesNotOverwriteAlreadyOpen()
    {
        // If a file is already in the workspace host (e.g. normal didOpen arrived later),
        // the buffer drain must not overwrite it.
        var root = Root("ws");
        var uri = new FileHelper(new MockFileSystem()).PathToFileUri(Path.Combine(root, "units.xml"));
        var fs = new MockFileSystem();
        fs.AddDirectory(root);
        var host = new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance);
        host.AddOrUpdate(uri, "<AlreadyOpen/>", 1);
        var buffer = new FakePreOpenBuffer((uri, "<BufferVersion/>", 0));
        var svc = new FakeIndexService();

        await BuildWithBuffer(fs, svc, host, [root], buffer).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.True(host.TryGet(uri, out var doc));
        Assert.Equal("<AlreadyOpen/>", doc.Text);
    }

    [Fact(Skip = "This test is disabled. It covers a code path that is currently disabled and broken.")]
    public async Task ScanAsync_ParseableFiles_AreAddedToWorkspaceHost()
    {
        // Files that were bulk-scanned must land in the workspace host so that hover,
        // completion, and other handlers that call _workspaceHost.TryGet can serve
        // requests without requiring a prior textDocument/didOpen from the client.
        var root = Root("ws");
        var content = "<Root><Unit Name=\"X-Wing\"/></Root>";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "units.xml")] = new(content)
        });
        var host = new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance);
        var svc = new FakeIndexService();

        await Build(fs, svc, host, [root], new FakeParser()).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        var uri = new FileHelper(fs).PathToFileUri(Path.Combine(root, "units.xml"));
        Assert.True(host.TryGet(uri, out var doc));
        Assert.Equal(content, doc.Text);
        Assert.Equal(0, doc.Version);
    }

    [Fact]
    public async Task ScanAsync_ParseableFiles_DoNotOverwriteAlreadyOpenDocument()
    {
        // If the client already sent textDocument/didOpen before the scan runs
        // (workspace host has version > 0), the scan must not overwrite editor content
        // with the on-disk version (which may lag behind unsaved edits).
        var root = Root("ws");
        var diskContent = "<Root/>";
        var editorContent = "<Root><Unit Name=\"Edited\"/></Root>";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "units.xml")] = new(diskContent)
        });
        var host = new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance);
        var uri = new FileHelper(fs).PathToFileUri(Path.Combine(root, "units.xml"));
        host.AddOrUpdate(uri, editorContent, 1);
        var svc = new FakeIndexService();

        await Build(fs, svc, host, [root], new FakeParser()).ScanAsync(WorkspaceConfiguration.Empty, [root], CancellationToken.None);

        Assert.True(host.TryGet(uri, out var doc));
        Assert.Equal(editorContent, doc.Text);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakePreOpenBuffer : IPreOpenBuffer
    {
        private readonly IReadOnlyList<(string Uri, string Text, int Version)> _items;

        public FakePreOpenBuffer(params (string Uri, string Text, int Version)[] items)
        {
            _items = items;
        }

        public void RecordOpen(string uri, string text, int version)
        {
        }

        public IReadOnlyList<(string Uri, string Text, int Version)> DrainAndClose()
        {
            return _items;
        }
    }

    private sealed class FakeParser : IGameDocumentParser
    {
        private readonly string _ext;

        public FakeParser(string ext = ".xml")
        {
            _ext = ext;
        }

        public bool CanParse(string ext)
        {
            return ext.Equals(_ext, StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<DocumentIndex> ParseAsync(string uri, string text, int version, CancellationToken ct)
        {
            throw new NotSupportedException();
        }
    }

    // Schema provider that is immediately ready but starts with no metafiles.
    // Calling Refresh() updates the metafiles and fires SchemaRefreshed, simulating a
    // schema hot-reload or a late HTTP fetch completing after the initial scan.
    private sealed class RefreshableSchemaProvider : ISchemaProvider
    {
        private MetafileDefinition[] _metafiles = [];
        public event EventHandler? SchemaRefreshed;
        public Task ReadyAsync => Task.CompletedTask;
        public IReadOnlyList<MetafileDefinition> AllMetafiles => _metafiles;
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];

        public XmlTagDefinition? GetTag(string t)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string t)
        {
            return [];
        }

        public GameObjectTypeDefinition? GetObjectType(string t)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string t)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string e)
        {
            return null;
        }

        public void Refresh(params MetafileDefinition[] metafiles)
        {
            _metafiles = metafiles;
            SchemaRefreshed?.Invoke(this, EventArgs.Empty);
        }
    }

    // Schema provider whose ReadyAsync blocks until Release() is called. Fires WhenWaiting
    // as soon as the ReadyAsync task is accessed so tests can synchronise on the moment
    // WaitForSchemaAsync starts blocking.
    private sealed class BlockingSchemaProvider : ISchemaProvider
    {
        private readonly TaskCompletionSource _waitingTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _readyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WhenWaiting => _waitingTcs.Task;

        public Task ReadyAsync
        {
            get
            {
                _waitingTcs.TrySetResult();
                return _readyTcs.Task;
            }
        }

        public void Release() => _readyTcs.TrySetResult();

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];

        public XmlTagDefinition? GetTag(string t) => null;
        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string t) => [];
        public GameObjectTypeDefinition? GetObjectType(string t) => null;
        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string t) => [];
        public EnumDefinition? GetEnum(string e) => null;
    }

    // Schema provider that starts empty and completes ReadyAsync when LoadMetafiles is called,
    // mimicking HttpSchemaProvider's background-load behaviour.
    private sealed class DelayedSchemaProvider : ISchemaProvider
    {
        private readonly TaskCompletionSource _readyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private MetafileDefinition[] _metafiles = [];
        public event EventHandler? SchemaRefreshed;

        public Task ReadyAsync => _readyTcs.Task;
        public IReadOnlyList<MetafileDefinition> AllMetafiles => _metafiles;
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];

        public XmlTagDefinition? GetTag(string t)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string t)
        {
            return [];
        }

        public GameObjectTypeDefinition? GetObjectType(string t)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string t)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string e)
        {
            return null;
        }

        public void LoadMetafiles(params MetafileDefinition[] metafiles)
        {
            _metafiles = metafiles;
            SchemaRefreshed?.Invoke(this, EventArgs.Empty);
            _readyTcs.TrySetResult();
        }
    }

    private sealed class FakeSchemaProvider : ISchemaProvider
    {
        private readonly MetafileDefinition[] _metafiles;

        public FakeSchemaProvider(params MetafileDefinition[] metafiles)
        {
            _metafiles = metafiles;
        }

        public IReadOnlyList<MetafileDefinition> AllMetafiles => _metafiles;
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];

        // Fire immediately on subscription: simulates a schema that is already loaded
        // so that WaitForSchemaAsync in WorkspaceScanner does not time out in tests.
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
            return null;
        }
    }

    private sealed class FakeIndexService : IGameIndexService
    {
        private readonly object _lock = new();
        public readonly List<(string Uri, int Version)> Calls = [];
        private int _callTarget;

        private TaskCompletionSource? _callTcs;
        public int BeginBulkUpdateCallCount;

        public FakeIndexService(GameIndex? current = null)
        {
            Current = current ?? GameIndex.Empty;
        }

        public GameIndex Current { get; }

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_lock)
            {
                Calls.Add((uri, version));
                if (_callTcs is not null && Calls.Count >= _callTarget)
                    _callTcs.TrySetResult();
            }

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

        public IAssetFileIndex? AppliedAssetFiles { get; private set; }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
            AppliedAssetFiles = index;
        }

        public ImmutableDictionary<string, ImmutableArray<string>>? AppliedModelBones { get; private set; }

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
            AppliedModelBones = bones;
        }

        public IDisposable BeginBulkUpdate()
        {
            Interlocked.Increment(ref BeginBulkUpdateCallCount);
            return NullDisposable.Instance;
        }

        public Task WhenCallCountReaches(int n)
        {
            lock (_lock)
            {
                if (Calls.Count >= n) return Task.CompletedTask;
                _callTarget = n;
                _callTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return _callTcs.Task;
            }
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}

file sealed class NullLocalisationLoader : ILocalisationLoader
{
    public Task LoadAsync(WorkspaceConfiguration workspaceConfig, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}