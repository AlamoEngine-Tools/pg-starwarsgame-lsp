// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

public sealed class RoutedGameIndexServiceTest
{
    private static readonly FileHelper Helper = new(new MockFileSystem());

    private static readonly string ModA = Path.Combine(Root(), "mods", "moda");
    private static readonly string ModB = Path.Combine(Root(), "mods", "modb");
    private static readonly string Shared = Path.Combine(Root(), "mods", "shared");

    private static string Root()
    {
        return Path.GetPathRoot(Path.GetFullPath("."))!;
    }

    private static string Uri(params string[] segments)
    {
        return Helper.NormalizeUri(Helper.PathToFileUri(Path.Combine(segments)));
    }

    private static WorkspaceConfiguration Config(string projectDir, params string[] extraXmlDirs)
    {
        var xml = new List<string> { Path.Combine(projectDir, "data", "xml") };
        xml.AddRange(extraXmlDirs);
        var name = Path.GetFileName(projectDir);
        return new WorkspaceConfiguration(xml, [], [], [], null)
        {
            ProjectPath = Path.Combine(projectDir, name + ".pgproj").Replace('\\', '/').ToLowerInvariant(),
            Layers = [new ProjectLayer(0, name, xml, [], [], [], null)]
        };
    }

    private static (ProjectRegistry Registry, RoutedGameIndexService Index) Build()
    {
        var registry = new ProjectRegistry(Helper, [], NullLoggerFactory.Instance);
        return (registry, new RoutedGameIndexService(registry));
    }

    private static DocumentIndex Document(string uri, string symbolId)
    {
        return new DocumentIndex(uri, 1,
            [new GameSymbol(symbolId, GameSymbolKind.XmlObject, null, new FileOrigin(uri, 0, 0), null)],
            []);
    }

    [Fact]
    public void InjectDocument_RoutesTheDocumentToItsOwningProjectOnly()
    {
        var (registry, index) = Build();
        registry.SetProjects([Config(ModA), Config(ModB)]);
        var uri = Uri(ModA, "data", "xml", "units.xml");

        index.InjectDocument(Document(uri, "REBEL_TROOPER"));

        Assert.True(index.For(uri).Documents.ContainsKey(uri));
        Assert.False(registry.Resolve(Uri(ModB, "data", "xml", "units.xml"))[0]
            .Index.Current.Documents.ContainsKey(uri));
    }

    [Fact]
    public void For_TwoProjectsDefiningTheSameName_KeepsThemIsolated()
    {
        // The core promise of multi-root: unrelated mods must not cross-resolve.
        var (registry, index) = Build();
        registry.SetProjects([Config(ModA), Config(ModB)]);
        var a = Uri(ModA, "data", "xml", "units.xml");
        var b = Uri(ModB, "data", "xml", "units.xml");

        index.InjectDocument(Document(a, "REBEL_TROOPER"));
        index.InjectDocument(Document(b, "REBEL_TROOPER"));

        Assert.Single(index.For(a).WorkspaceDefinitions["REBEL_TROOPER"]);
        Assert.Single(index.For(b).WorkspaceDefinitions["REBEL_TROOPER"]);
        Assert.Equal(a, OriginUri(index.For(a).WorkspaceDefinitions["REBEL_TROOPER"][0]));
        Assert.Equal(b, OriginUri(index.For(b).WorkspaceDefinitions["REBEL_TROOPER"][0]));
        return;

        static string? OriginUri(GameSymbol symbol)
        {
            return symbol.Origin is FileOrigin origin ? origin.Uri : null;
        }
    }

    [Fact]
    public void InjectDocument_SharedDependencyFile_IsIndexedIntoEveryOwningProject()
    {
        var sharedXml = Path.Combine(Shared, "data", "xml");
        var (registry, index) = Build();
        registry.SetProjects([Config(ModA, sharedXml), Config(ModB, sharedXml)]);
        var uri = Uri(sharedXml, "units.xml");

        index.InjectDocument(Document(uri, "SHARED_UNIT"));

        foreach (var workspace in registry.All)
            Assert.True(workspace.Index.Current.Documents.ContainsKey(uri));
    }

    [Fact]
    public void RemoveDocument_RemovesFromEveryProjectThatHadIt()
    {
        var sharedXml = Path.Combine(Shared, "data", "xml");
        var (registry, index) = Build();
        registry.SetProjects([Config(ModA, sharedXml), Config(ModB, sharedXml)]);
        var uri = Uri(sharedXml, "units.xml");
        index.InjectDocument(Document(uri, "SHARED_UNIT"));

        index.RemoveDocument(uri);

        foreach (var workspace in registry.All)
            Assert.False(workspace.Index.Current.Documents.ContainsKey(uri));
    }

    [Fact]
    public void ApplyBaseline_FansOutToEveryProject()
    {
        // Base-game data is shared: it is the whole reason this is one server, not one per folder.
        var (registry, index) = Build();
        registry.SetProjects([Config(ModA), Config(ModB)]);
        var baseline = BaselineIndex.Empty with { SourceManifestHash = "test-hash" };

        index.ApplyBaseline(baseline);

        foreach (var workspace in registry.All)
            Assert.Same(baseline, workspace.Index.Current.Baseline);
    }

    [Fact]
    public void ApplyWorkspaceDynamicEnumValues_FansOutToEveryProject()
    {
        var (registry, index) = Build();
        registry.SetProjects([Config(ModA), Config(ModB)]);
        var values = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("SFXEventType", ["Foo"]);

        index.ApplyWorkspaceDynamicEnumValues(values);

        foreach (var workspace in registry.All)
            Assert.Contains("SFXEventType", workspace.Index.Current.WorkspaceDynamicEnumValues.Keys);
    }

    [Fact]
    public void Current_ReturnsThePrimaryProjectsIndex()
    {
        var (registry, index) = Build();
        registry.SetProjects([Config(ModA), Config(ModB)]);
        var uri = Uri(ModA, "data", "xml", "units.xml");
        index.InjectDocument(Document(uri, "REBEL_TROOPER"));

        Assert.True(index.Current.Documents.ContainsKey(uri));
    }

    [Fact]
    public void For_FileOutsideEveryProject_FallsBackToThePrimaryIndex()
    {
        var (registry, index) = Build();
        registry.SetProjects([Config(ModA)]);

        Assert.Same(registry.Primary.Index.Current, index.For(Uri(Root(), "elsewhere", "notes.xml")));
    }

    [Fact]
    public void IndexChanged_FiresForEveryProjectIncludingOnesAddedLater()
    {
        var (registry, index) = Build();
        registry.SetProjects([Config(ModA)]);
        var fired = 0;
        index.IndexChanged += _ => Interlocked.Increment(ref fired);

        registry.SetProjects([Config(ModA), Config(ModB)]);
        index.InjectDocument(Document(Uri(ModB, "data", "xml", "units.xml"), "X"));

        Assert.True(fired > 0, "a project added after subscription must still raise IndexChanged");
    }

    [Fact]
    public void BeginBulkUpdate_SuppressesAcrossEveryProject()
    {
        var (registry, index) = Build();
        registry.SetProjects([Config(ModA), Config(ModB)]);
        var fired = 0;
        index.IndexChanged += _ => Interlocked.Increment(ref fired);

        using (index.BeginBulkUpdate())
        {
            index.InjectDocument(Document(Uri(ModA, "data", "xml", "a.xml"), "A"));
            index.InjectDocument(Document(Uri(ModB, "data", "xml", "b.xml"), "B"));
            Assert.Equal(0, fired);
        }

        Assert.True(fired > 0);
    }
}
