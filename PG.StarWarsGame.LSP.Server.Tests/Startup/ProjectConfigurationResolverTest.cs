// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Tests.Startup;

public sealed class ProjectConfigurationResolverTest
{
    private static readonly string DriveRoot = Path.GetPathRoot(Path.GetFullPath("."))!;
    private static readonly string WorkspaceRoot = Path.Combine(DriveRoot, "mods", "mymod");
    private static readonly string ProjectPath = Path.Combine(WorkspaceRoot, "mymod.pgproj");
    private static readonly string OtherWorkspaceRoot = Path.Combine(DriveRoot, "mods", "othermod");
    private static readonly string OtherProjectPath = Path.Combine(OtherWorkspaceRoot, "othermod.pgproj");

    private static string AbsLower(string rel)
    {
        return Path.GetFullPath(Path.Combine(WorkspaceRoot, rel)).Replace('\\', '/').ToLowerInvariant();
    }

    private static string OtherAbsLower(string rel)
    {
        return Path.GetFullPath(Path.Combine(OtherWorkspaceRoot, rel)).Replace('\\', '/').ToLowerInvariant();
    }

    // Returned as the interface so the Resolve default implementation is in scope.
    private static IProjectConfigurationResolver Build(MockFileSystem fs)
    {
        return Build(fs, out _);
    }

    private static IProjectConfigurationResolver Build(MockFileSystem fs, out RecordingUserNotifier notifier)
    {
        var fileHelper = new FileHelper(fs);
        var loader = new ModProjectLoader(fileHelper, NullLogger<ModProjectLoader>.Instance);
        var graph = new ProjectDependencyGraph(NullLogger<ProjectDependencyGraph>.Instance);
        var resolver = new ModProjectResolver(fileHelper, loader, graph, NullLogger<ModProjectResolver>.Instance);
        var detector = new ModProjectDetector(fileHelper, NullLogger<ModProjectDetector>.Instance);
        notifier = new RecordingUserNotifier();
        return new ProjectConfigurationResolver(detector, loader, resolver, notifier,
            NullLogger<ProjectConfigurationResolver>.Instance);
    }

    [Fact]
    public void Resolve_NoProjectFile_ReturnsNull()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(WorkspaceRoot);

        var config = Build(fs).Resolve([WorkspaceRoot]);

        Assert.Null(config);
    }

    [Fact]
    public void Resolve_ProjectFileFound_ReturnsResolvedConfig()
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

        var config = Build(fs).Resolve([WorkspaceRoot]);

        Assert.NotNull(config);
        Assert.Contains(AbsLower("data/xml"), config!.XmlDirectories);
        Assert.Contains(AbsLower("data/scripts"), config.ScriptRoots);
    }

    [Fact]
    public void Resolve_MalformedProjectFile_ReturnsNull()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectPath] = new("{ this is not valid json ")
        });

        var config = Build(fs).Resolve([WorkspaceRoot]);

        Assert.Null(config);
    }

    [Fact]
    public void Resolve_MultiplePgprojFiles_ReturnsNullAndShowsUserErrorNotification()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(WorkspaceRoot, "a.pgproj")] = new("{}"),
            [Path.Combine(WorkspaceRoot, "b.pgproj")] = new("{}")
        });

        var config = Build(fs, out var notifier).Resolve([WorkspaceRoot]);

        Assert.Null(config);
        var message = Assert.Single(notifier.Errors);
        Assert.Contains("a.pgproj", message);
        Assert.Contains("b.pgproj", message);
    }

    [Fact]
    public void ResolveAll_TwoRootsEachWithAProject_ReturnsBothConfigurations()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectPath] = new("""
                                {
                                  "modinfo": { "name": "My Mod" },
                                  "directories": { "xml": ["data/xml"] }
                                }
                                """),
            [OtherProjectPath] = new("""
                                     {
                                       "modinfo": { "name": "Other Mod" },
                                       "directories": { "xml": ["data/xml"] }
                                     }
                                     """)
        });

        var configs = Build(fs).ResolveAll([WorkspaceRoot, OtherWorkspaceRoot]);

        Assert.Equal(2, configs.Count);
        Assert.Contains(configs, c => c.XmlDirectories.Contains(AbsLower("data/xml")));
        Assert.Contains(configs, c => c.XmlDirectories.Contains(OtherAbsLower("data/xml")));
    }

    [Fact]
    public void ResolveAll_StampsEachConfigurationWithItsProjectPath()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectPath] = new("""{ "modinfo": { "name": "My Mod" } }"""),
            [OtherProjectPath] = new("""{ "modinfo": { "name": "Other Mod" } }""")
        });

        var configs = Build(fs).ResolveAll([WorkspaceRoot, OtherWorkspaceRoot]);

        var paths = configs.Select(c => c.ProjectPath).ToList();
        Assert.Contains(ProjectPath.Replace('\\', '/').ToLowerInvariant(), paths);
        Assert.Contains(OtherProjectPath.Replace('\\', '/').ToLowerInvariant(), paths);
    }

    [Fact]
    public void ResolveAll_OneProjectFailsToLoad_KeepsTheOtherAndNotifies()
    {
        // One broken mod in a multi-root workspace must not take the healthy ones down with it.
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectPath] = new("{ this is not valid json "),
            [OtherProjectPath] = new("""
                                     {
                                       "modinfo": { "name": "Other Mod" },
                                       "directories": { "xml": ["data/xml"] }
                                     }
                                     """)
        });

        var configs = Build(fs, out var notifier).ResolveAll([WorkspaceRoot, OtherWorkspaceRoot]);

        var config = Assert.Single(configs);
        Assert.Contains(OtherAbsLower("data/xml"), config.XmlDirectories);
        Assert.Single(notifier.Errors);
    }

    [Fact]
    public void ResolveAll_NoProjects_ReturnsEmpty()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(WorkspaceRoot);

        Assert.Empty(Build(fs).ResolveAll([WorkspaceRoot]));
    }

    [Fact]
    public void Resolve_TwoProjects_ReturnsTheFirstForBackwardsCompatibility()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectPath] = new("""{ "modinfo": { "name": "My Mod" }, "directories": { "xml": ["data/xml"] } }"""),
            [OtherProjectPath] = new("""{ "modinfo": { "name": "Other Mod" } }""")
        });

        var config = Build(fs).Resolve([WorkspaceRoot, OtherWorkspaceRoot]);

        Assert.NotNull(config);
        Assert.Contains(AbsLower("data/xml"), config!.XmlDirectories);
    }

    [Fact]
    public void Resolve_MalformedProjectFile_ShowsUserErrorNotification()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectPath] = new("""{ "projectReferences": { "path": "../core/core.pgproj" } }""")
        });

        var config = Build(fs, out var notifier).Resolve([WorkspaceRoot]);

        Assert.Null(config);
        var message = Assert.Single(notifier.Errors);
        Assert.Contains("mymod.pgproj", message);
        Assert.Contains("projectReferences", message, StringComparison.OrdinalIgnoreCase);
    }

}