// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
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
        return p.Replace('\\', '/');
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
    public void Resolve_KeepsTheSpellingOfTheProjectAndItsDirectories()
    {
        // Every file under a root is handed out by a URI built from it; a folded root names a file
        // the host does not have, and a case-sensitive file system none at all.
        var dir = Path.Combine(DriveRoot, "Mods", "Root");
        var path = Path.Combine(dir, "Root.pgproj");
        const string json = """
                            {
                              "modinfo": { "name": "Root" },
                              "directories": { "xml": ["Data/Xml"] }
                            }
                            """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [path] = new(json) });
        var fileHelper = new FileHelper(fs);
        var loader = new ModProjectLoader(fileHelper, NullLogger<ModProjectLoader>.Instance,
            new NullPgprojMigrationSink(), PgprojMigrations.All, ResxMessageCatalog.Server("en"));
        var resolver = new ModProjectResolver(fileHelper, loader,
            new ProjectDependencyGraph(NullLogger<ProjectDependencyGraph>.Instance),
            NullLogger<ModProjectResolver>.Instance, ResxMessageCatalog.Server("en"));

        var config = resolver.Resolve(path, loader.Load(path));

        Assert.Equal([Normalize(Path.Combine(dir, "Data", "Xml"))], config.XmlDirectories);
        Assert.Equal(Normalize(path), config.Layers.Single().ProjectPath);
    }

    [Fact]
    public void Resolve_SpellsADirectoryAsTheDiskDoes_WhereTheProjectFileSpellsItOtherwise()
    {
        // The vanilla projects say "data/xml" of a folder the game ships as "Data/XML" (#291): the
        // host knows the file by the disk's name, so a URI under the other spelling is a second
        // document to it. A directory that does not exist keeps the project file's spelling.
        var dir = Path.Combine(DriveRoot, "Mods", "Root");
        var path = Path.Combine(dir, "Root.pgproj");
        const string json = """
                            {
                              "modinfo": { "name": "Root" },
                              "directories": { "xml": ["data/xml"], "scripts": ["data/Scripts"] }
                            }
                            """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [path] = new(json) });
        fs.AddDirectory(Path.Combine(dir, "Data", "XML"));
        var fileHelper = new FileHelper(fs);
        var loader = new ModProjectLoader(fileHelper, NullLogger<ModProjectLoader>.Instance,
            new NullPgprojMigrationSink(), PgprojMigrations.All, ResxMessageCatalog.Server("en"));
        var resolver = new ModProjectResolver(fileHelper, loader,
            new ProjectDependencyGraph(NullLogger<ProjectDependencyGraph>.Instance),
            NullLogger<ModProjectResolver>.Instance, ResxMessageCatalog.Server("en"));

        var config = resolver.Resolve(path, loader.Load(path));

        Assert.Equal([Normalize(Path.Combine(dir, "Data", "XML"))], config.XmlDirectories);
        Assert.Equal([Normalize(Path.Combine(dir, "Data", "Scripts"))], config.ScriptRoots);
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

    // The flattened lists lose whether a layer was named directly or pulled in; the shape keeps it.
    [Fact]
    public void Resolve_CarriesTheDependencyShape()
    {
        const string rootJson = """
                                {
                                  "modinfo": { "name": "Root" },
                                  "projectReferences": [ { "path": "../dep/dep.pgproj" } ]
                                }
                                """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new(rootJson),
            [DepPath] = new("""{ "modinfo": { "name": "Dep" } }""")
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        Assert.Equal(new ProjectDependencyShape(1, 1, 1, 0), config.Dependencies);
    }

    [Fact]
    public void Resolve_StoryDialogRoots_ResolvedPerLayer_AndUnionedDependencyFirst()
    {
        const string depJson = """
                               {
                                 "modinfo": { "name": "Dep" },
                                 "directories": { "storyDialog": ["data/scripts/story"] }
                               }
                               """;
        const string rootJson = """
                                {
                                  "modinfo": { "name": "Root" },
                                  "directories": { "storyDialog": ["data/scripts/story"] },
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

        var depDialog = Abs(DepDir, "data/scripts/story");
        var rootDialog = Abs(RootDir, "data/scripts/story");
        Assert.Equal(new[] { depDialog, rootDialog }, config.StoryDialogRoots);
        Assert.Equal([depDialog], config.Layers[0].StoryDialogRoots);
        Assert.Equal([rootDialog], config.Layers[1].StoryDialogRoots);
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
    public void Resolve_LocalisationDirectory_MapsToTextRoots()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Root" },
                              "localisation": { "type": "CSV", "directory": "data/text" }
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
                              "localisation": { "type": "DAT", "directory": "data/text" }
                            }
                            """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new(json)
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        Assert.Equal("DAT", config.TextResourceType);
    }

    [Fact]
    public void Resolve_TextResourceType_RootProjectWinsOverDependency()
    {
        const string depJson = """
                               {
                                 "modinfo": { "name": "Dep" },
                                 "localisation": { "type": "DAT", "directory": "data/text" }
                               }
                               """;
        const string rootJson = """
                                {
                                  "modinfo": { "name": "Root" },
                                  "localisation": { "type": "CSV", "directory": "data/text" },
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

        Assert.Equal("CSV", config.TextResourceType);
    }

    [Fact]
    public void Resolve_TextResourceType_NullWhenAbsent()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Root" },
                              "directories": { "xml": ["data/xml"] }
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
    public void Resolve_NoLocalisationNode_TextRootsEmpty()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Root" },
                              "directories": { "xml": ["data/xml"] }
                            }
                            """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new(json)
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        Assert.Empty(config.TextRoots);
    }

    [Fact]
    public void Resolve_SingleProject_EmitsOneLayerRankZero()
    {
        const string json = """
                            {
                              "name": "Root",
                              "directories": { "xml": ["data/xml"] },
                              "localisation": { "type": "CSV", "directory": "data/text" }
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
        Assert.Equal("CSV", layer.TextResourceType);
    }

    [Fact]
    public void Resolve_WithDependency_EmitsLayersDependencyRankZeroRootHighest_PerLayerResourceType()
    {
        const string depJson = """
                               {
                                 "name": "Dep",
                                 "directories": { "xml": ["data/xml"] },
                                 "localisation": { "type": "CSV", "directory": "data/text" }
                               }
                               """;
        const string rootJson = """
                                {
                                  "name": "Root",
                                  "directories": { "xml": ["data/xml"] },
                                  "localisation": { "type": "XML", "directory": "data/text" },
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
        // The dependency keeps its OWN resource type - not collapsed to the root's. This is the
        // fix for dependency .csv text being skipped when the root declares a different type.
        Assert.Equal("CSV", config.Layers[0].TextResourceType);
        Assert.Contains(Abs(DepDir, "data/text"), config.Layers[0].TextRoots);

        Assert.Equal(1, config.Layers[1].Rank);
        Assert.Equal("Root", config.Layers[1].Name);
        Assert.Equal("XML", config.Layers[1].TextResourceType);
    }

    // ── the target game ──────────────────────────────────────────────────────

    [Fact]
    public void Resolve_CarriesTheRootProjectsGame()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new("""{ "name": "Root", "game": "eaw" }""")
        });
        var (resolver, root) = Build(fs);

        Assert.Equal(TargetGame.EAW, resolver.Resolve(RootPath, root).Game);
    }

    // The base game cannot load the expansion's data, so an EaW mod built on a FoC project
    // describes a game that does not exist.
    [Fact]
    public void Resolve_EawProjectReferencingAFocProject_RefusesNamingTheDependency()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new("""
                             { "name": "Root", "game": "eaw",
                               "projectReferences": [ { "path": "../dep/dep.pgproj" } ] }
                             """),
            [DepPath] = new("""{ "name": "Expansion Library", "game": "foc" }""")
        });
        var (resolver, root) = Build(fs);

        var ex = Assert.Throws<ModProjectLoadException>(() => resolver.Resolve(RootPath, root));

        Assert.Contains("Expansion Library", ex.Message, StringComparison.Ordinal);
        Assert.Equal(ProjectProblem.Invalid, ex.Problem);
    }

    // The expansion reads the base game's data, so a FoC mod may build on an EaW project.
    [Fact]
    public void Resolve_FocProjectReferencingAnEawProject_Resolves()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [RootPath] = new("""
                             { "name": "Root", "game": "foc",
                               "projectReferences": [ { "path": "../dep/dep.pgproj" } ] }
                             """),
            [DepPath] = new("""{ "name": "Base Library", "game": "eaw" }""")
        });
        var (resolver, root) = Build(fs);

        var config = resolver.Resolve(RootPath, root);

        Assert.Equal(TargetGame.FOC, config.Game);
        Assert.Equal(2, config.Layers.Count);
    }

    private static (ModProjectResolver Resolver, ModProjectFile Root) Build(MockFileSystem fs)
    {
        var fileHelper = new FileHelper(fs);
        var loader = new ModProjectLoader(fileHelper, NullLogger<ModProjectLoader>.Instance,
            new NullPgprojMigrationSink(), PgprojMigrations.All, ResxMessageCatalog.Server("en"));
        var graph = new ProjectDependencyGraph(NullLogger<ProjectDependencyGraph>.Instance);
        var resolver = new ModProjectResolver(
            fileHelper, loader, graph, NullLogger<ModProjectResolver>.Instance, ResxMessageCatalog.Server("en"));
        var root = loader.Load(RootPath);
        return (resolver, root);
    }
}