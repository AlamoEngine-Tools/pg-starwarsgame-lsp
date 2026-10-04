// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Server.Status;

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

    private static ProjectConfigurationResolver Build(MockFileSystem fs, out RecordingUserNotifier notifier,
        ServerStatusRecorder? status = null, LspConfiguration? configuration = null)
    {
        var fileHelper = new FileHelper(fs);
        var loader = new ModProjectLoader(fileHelper, NullLogger<ModProjectLoader>.Instance);
        var graph = new ProjectDependencyGraph(NullLogger<ProjectDependencyGraph>.Instance);
        var resolver = new ModProjectResolver(fileHelper, loader, graph, NullLogger<ModProjectResolver>.Instance);
        var detector = new ModProjectDetector(fileHelper, NullLogger<ModProjectDetector>.Instance);
        notifier = new RecordingUserNotifier();
        return new ProjectConfigurationResolver(detector, loader, resolver, notifier,
            NullLogger<ProjectConfigurationResolver>.Instance, status,
            configuration is null ? null : new FakeLspConfigurationProvider { Current = configuration });
    }

    // ── what the bug report is told ──────────────────────────────────────────
    //
    // The user sees the message, which names the file and its path. The report carries the
    // category instead, so it says what went wrong without saying where.

    [Fact]
    public void Resolve_NoProjectFile_RecordsMissing()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(WorkspaceRoot);
        var status = new ServerStatusRecorder();

        Build(fs, out _, status).Resolve([WorkspaceRoot]);

        Assert.False(status.ProjectDetected);
        Assert.Equal(ProjectProblem.Missing, status.ProjectProblem);
    }

    [Theory]
    [InlineData("""{ "modinfo": { "name": "My Mod" }, "directories": { "xml": ["data/xml"] } }""", ProjectProblem.None)]
    [InlineData("{ not json", ProjectProblem.Unparseable)]
    [InlineData("[]", ProjectProblem.Unparseable)]
    [InlineData("""{ "_type": "aetswg.ModProject", "_typeVersion": "aetswg-99" }""", ProjectProblem.UnsupportedVersion)]
    [InlineData("""{ "modinfo": { "name": "My Mod" }, "icons": { "megaTexture": "" } }""", ProjectProblem.Invalid)]
    public void Resolve_OneProjectFile_RecordsItsCategory(string json, ProjectProblem expected)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [ProjectPath] = new(json) });
        var status = new ServerStatusRecorder();

        Build(fs, out _, status).Resolve([WorkspaceRoot]);

        Assert.True(status.ProjectDetected);
        Assert.Equal(expected, status.ProjectProblem);
    }

    /// <summary>
    ///     Several project files under one root used to be refused outright. One server runs per
    ///     project now, so a client that names no project gets the shallowest one, a warning says
    ///     how many others there are, and the status carries the count.
    /// </summary>
    [Fact]
    public void Resolve_TwoProjectFiles_LoadsTheShallowest_WarnsAndCountsTheOther()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectPath] = new("{}"),
            [Path.Combine(WorkspaceRoot, "sub", "other.pgproj")] = new("{}")
        });
        var status = new ServerStatusRecorder();

        var config = Build(fs, out var notifier, status).Resolve([WorkspaceRoot]);

        Assert.NotNull(config);
        Assert.Equal(AbsLower("mymod.pgproj"), config.Layers.Single().ProjectPath);
        Assert.True(status.ProjectDetected);
        Assert.Equal(ProjectProblem.None, status.ProjectProblem);
        Assert.Equal(1, status.OtherProjectFiles);
        Assert.Single(notifier.Warnings);
        Assert.Empty(notifier.Errors);
    }

    [Fact]
    public void Resolve_ExplicitProjectPath_LoadsThatFile_WithoutDetection()
    {
        var other = Path.Combine(WorkspaceRoot, "sub", "other.pgproj");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectPath] = new("{}"),
            [other] = new("{}")
        });
        var status = new ServerStatusRecorder();

        var config = Build(fs, out var notifier, status, new LspConfiguration { ProjectPath = other })
            .Resolve([WorkspaceRoot]);

        Assert.NotNull(config);
        Assert.Equal(AbsLower(Path.Combine("sub", "other.pgproj")), config.Layers.Single().ProjectPath);
        // Named explicitly, so the sibling is not "another project file" worth a warning.
        Assert.Equal(0, status.OtherProjectFiles);
        Assert.Empty(notifier.Warnings);
    }

    [Fact]
    public void Resolve_ExplicitProjectPathThatDoesNotExist_IsAnError()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(WorkspaceRoot);
        var status = new ServerStatusRecorder();

        var config = Build(fs, out var notifier, status,
            new LspConfiguration { ProjectPath = Path.Combine(WorkspaceRoot, "gone.pgproj") }).Resolve([WorkspaceRoot]);

        Assert.Null(config);
        Assert.Single(notifier.Errors);
        Assert.Equal(ProjectProblem.Missing, status.ProjectProblem);
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
    public void Resolve_TwoProjectFilesAtTheSameDepth_LoadsTheFirstByNameAndWarns()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(WorkspaceRoot, "a.pgproj")] = new("{}"),
            [Path.Combine(WorkspaceRoot, "b.pgproj")] = new("{}")
        });

        var config = Build(fs, out var notifier).Resolve([WorkspaceRoot]);

        Assert.NotNull(config);
        Assert.Equal(AbsLower("a.pgproj"), config.Layers.Single().ProjectPath);
        Assert.Empty(notifier.Errors);
        var message = Assert.Single(notifier.Warnings);
        Assert.Contains("a.pgproj", message);
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