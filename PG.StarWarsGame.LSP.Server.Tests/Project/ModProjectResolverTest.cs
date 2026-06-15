// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

public sealed class ModProjectResolverTest
{
    private static readonly string DriveRoot = Path.GetPathRoot(Path.GetFullPath("."))!;
    private static readonly string RootDir = Path.Combine(DriveRoot, "mods", "root");
    private static readonly string DepDir = Path.Combine(DriveRoot, "mods", "dep");
    private static readonly string RootPath = Path.Combine(RootDir, "root.pgproj");
    private static readonly string DepPath = Path.Combine(DepDir, "dep.pgproj");

    private static string Normalize(string p)
    {
        return p.Replace('\\', '/').ToLowerInvariant();
    }

    private static string Abs(string projectDir, string rel)
    {
        return Normalize(Path.GetFullPath(Path.Combine(projectDir, rel)));
    }

    [Fact]
    public void Resolve_SingleProject_PopulatesXmlAndScriptRoots()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Root" },
                              "directories": {
                                "xml": ["data/xml"],
                                "scripts": ["data/scripts"]
                              }
                            }
                            """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new(json)
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        Assert.Contains(Abs(RootDir, "data/xml"), config.XmlDirectories);
        Assert.Contains(Abs(RootDir, "data/scripts"), config.ScriptRoots);
    }

    [Fact]
    public void Resolve_DependencyDirectories_MergedDependencyFirst()
    {
        const string depJson = """
                               {
                                 "modinfo": { "name": "Dep" },
                                 "directories": { "xml": ["data/xml"] }
                               }
                               """;
        const string rootJson = """
                                {
                                  "modinfo": { "name": "Root" },
                                  "directories": { "xml": ["data/xml"] },
                                  "projectReferences": [ { "path": "../dep/dep.pgproj" } ]
                                }
                                """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new(rootJson),
            [DepPath] = new(depJson)
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        var depXml = Abs(DepDir, "data/xml");
        var rootXml = Abs(RootDir, "data/xml");
        Assert.Equal(2, config.XmlDirectories.Count);
        Assert.True(config.XmlDirectories.ToList().IndexOf(depXml)
                    < config.XmlDirectories.ToList().IndexOf(rootXml));
    }

    [Fact]
    public void Resolve_ArtAndAudio_BothMapToAssetRoots()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Root" },
                              "directories": {
                                "art": ["data/art"],
                                "audio": ["data/audio"]
                              }
                            }
                            """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new(json)
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        Assert.Contains(Abs(RootDir, "data/art"), config.AssetRoots);
        Assert.Contains(Abs(RootDir, "data/audio"), config.AssetRoots);
    }

    [Fact]
    public void Resolve_NoXmlEntry_XmlDirectoriesEmpty()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Root" },
                              "directories": { "scripts": ["data/scripts"] }
                            }
                            """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new(json)
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        Assert.Empty(config.XmlDirectories);
        Assert.NotNull(config.XmlDirectories);
    }

    [Fact]
    public void Resolve_TextEntries_MapToTextRoots()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Root" },
                              "directories": { "text": ["data/text"] }
                            }
                            """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new(json)
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        Assert.Contains(Abs(RootDir, "data/text"), config.TextRoots);
    }

    [Fact]
    public void Resolve_TextResourceType_TakenFromRootProject()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Root" },
                              "directories": {
                                "text": ["data/text"],
                                "textResourceType": "dat"
                              }
                            }
                            """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new(json)
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        Assert.Equal("dat", config.TextResourceType);
    }

    [Fact]
    public void Resolve_TextResourceType_RootProjectWinsOverDependency()
    {
        const string depJson = """
                               {
                                 "modinfo": { "name": "Dep" },
                                 "directories": { "text": ["data/text"], "textResourceType": "dat" }
                               }
                               """;
        const string rootJson = """
                                {
                                  "modinfo": { "name": "Root" },
                                  "directories": { "text": ["data/text"], "textResourceType": "csv" },
                                  "projectReferences": [ { "path": "../dep/dep.pgproj" } ]
                                }
                                """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new(rootJson),
            [DepPath] = new(depJson)
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        Assert.Equal("csv", config.TextResourceType);
    }

    [Fact]
    public void Resolve_TextResourceType_NullWhenAbsent()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Root" },
                              "directories": { "text": ["data/text"] }
                            }
                            """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new(json)
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        Assert.Null(config.TextResourceType);
    }

    [Fact]
    public void Resolve_SingleProject_EmitsOneLayerRankZero()
    {
        const string json = """
                            {
                              "name": "Root",
                              "directories": { "xml": ["data/xml"], "textResourceType": "csv" }
                            }
                            """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new(json)
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        var layer = Assert.Single(config.Layers);
        Assert.Equal(0, layer.Rank);
        Assert.Equal("Root", layer.Name);
        Assert.Contains(Abs(RootDir, "data/xml"), layer.XmlDirectories);
        Assert.Equal("csv", layer.TextResourceType);
    }

    [Fact]
    public void Resolve_WithDependency_EmitsLayersDependencyRankZeroRootHighest_PerLayerResourceType()
    {
        const string depJson = """
                               {
                                 "name": "Dep",
                                 "directories": {
                                   "xml": ["data/xml"], "text": ["data/text"], "textResourceType": "csv"
                                 }
                               }
                               """;
        const string rootJson = """
                                {
                                  "name": "Root",
                                  "directories": { "xml": ["data/xml"], "textResourceType": "xml" },
                                  "projectReferences": [ { "path": "../dep/dep.pgproj" } ]
                                }
                                """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new(rootJson),
            [DepPath] = new(depJson)
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        Assert.Equal(2, config.Layers.Count);
        Assert.Equal(0, config.Layers[0].Rank);
        Assert.Equal("Dep", config.Layers[0].Name);
        // The dependency keeps its OWN resource type — not collapsed to the root's. This is the
        // fix for dependency .csv text being skipped when the root declares a different type.
        Assert.Equal("csv", config.Layers[0].TextResourceType);
        Assert.Contains(Abs(DepDir, "data/text"), config.Layers[0].TextRoots);

        Assert.Equal(1, config.Layers[1].Rank);
        Assert.Equal("Root", config.Layers[1].Name);
        Assert.Equal("xml", config.Layers[1].TextResourceType);
    }

    private static (ModProjectResolver Resolver, ModProjectFile Root) Build(MockFileSystem fs)
    {
        var fileHelper = new FileHelper(fs);
        var loader = new ModProjectLoader(fileHelper, NullLogger<ModProjectLoader>.Instance);
        var graph = new ProjectDependencyGraph(NullLogger<ProjectDependencyGraph>.Instance);
        var resolver = new ModProjectResolver(
            fileHelper, loader, graph, NullLogger<ModProjectResolver>.Instance);
        var root = loader.Load(RootPath);
        return (resolver, root);
    }
}