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

    private static string AbsLower(string rel)
    {
        return Path.GetFullPath(Path.Combine(WorkspaceRoot, rel)).Replace('\\', '/').ToLowerInvariant();
    }

    private static ProjectConfigurationResolver Build(MockFileSystem fs)
    {
        return Build(fs, out _);
    }

    private static ProjectConfigurationResolver Build(MockFileSystem fs, out RecordingUserNotifier notifier)
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
    public void Resolve_NoProjectFile_ShowsUserErrorNotification()
    {
        // Without a .pgproj there is nothing to index and every answer the server could give would
        // be an empty one. That used to be a log line nobody reads, so the editor looked like it was
        // working and simply knew nothing.
        var fs = new MockFileSystem();
        fs.AddDirectory(WorkspaceRoot);

        Build(fs, out var notifier).Resolve([WorkspaceRoot]);

        var message = Assert.Single(notifier.Errors);
        Assert.Contains(".pgproj", message);
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