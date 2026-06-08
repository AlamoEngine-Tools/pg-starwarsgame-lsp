// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

public sealed class ProjectDependencyGraphTest
{
    private static ModProjectFile Project(params string[] references)
    {
        return new ModProjectFile(
            "Mod",
            null,
            new DirectoryMap(),
            references.Select(r => new ProjectReference(r)).ToList());
    }

    [Fact]
    public void Build_SingleProjectNoRefs_ReturnsRootOnly()
    {
        var root = Project();
        var graph = new ProjectDependencyGraph(NullLoggerFactory());

        var result = graph.Build("/mods/root/root.pgproj", root, _ => null);

        Assert.Single(result);
        Assert.Equal("/mods/root/root.pgproj", result[0].Path);
        Assert.Same(root, result[0].File);
    }

    [Fact]
    public void Build_LinearChain_DeepestFirstRootLast()
    {
        // root -> A -> B
        var b = Project();
        var a = Project("../b/b.pgproj");
        var root = Project("../a/a.pgproj");

        var files = new Dictionary<string, ModProjectFile>
        {
            ["/mods/a/a.pgproj"] = a,
            ["/mods/b/b.pgproj"] = b
        };

        var graph = new ProjectDependencyGraph(NullLoggerFactory());
        var result = graph.Build("/mods/root/root.pgproj", root,
            p => files.TryGetValue(p, out var f) ? f : null);

        var paths = result.Select(r => r.Path).ToList();
        Assert.Equal(
            ["/mods/b/b.pgproj", "/mods/a/a.pgproj", "/mods/root/root.pgproj"],
            paths);
    }

    [Fact]
    public void Build_Diamond_SharedDependencyAppearsOnce()
    {
        // root -> A, root -> B, A -> C, B -> C
        var c = Project();
        var a = Project("../c/c.pgproj");
        var b = Project("../c/c.pgproj");
        var root = Project("../a/a.pgproj", "../b/b.pgproj");

        var files = new Dictionary<string, ModProjectFile>
        {
            ["/mods/a/a.pgproj"] = a,
            ["/mods/b/b.pgproj"] = b,
            ["/mods/c/c.pgproj"] = c
        };

        var graph = new ProjectDependencyGraph(NullLoggerFactory());
        var result = graph.Build("/mods/root/root.pgproj", root,
            p => files.TryGetValue(p, out var f) ? f : null);

        var paths = result.Select(r => r.Path).ToList();
        Assert.Equal(4, paths.Count);
        Assert.Single(paths, p => p == "/mods/c/c.pgproj");
        // C is the deepest shared dep — appears first.
        Assert.Equal("/mods/c/c.pgproj", paths[0]);
        // Root is always last.
        Assert.Equal("/mods/root/root.pgproj", paths[^1]);
        // A before B (DFS visit order), both before root.
        Assert.True(paths.IndexOf("/mods/a/a.pgproj") < paths.IndexOf("/mods/b/b.pgproj"));
    }

    [Fact]
    public void Build_Cycle_BreaksAndWarns()
    {
        // root -> A -> root
        ModProjectFile? root = null;
        var a = Project("../root/root.pgproj");
        root = Project("../a/a.pgproj");

        var files = new Dictionary<string, ModProjectFile>
        {
            ["/mods/a/a.pgproj"] = a,
            ["/mods/root/root.pgproj"] = root
        };

        var logger = new ListLogger();
        var graph = new ProjectDependencyGraph(logger);
        var result = graph.Build("/mods/root/root.pgproj", root,
            p => files.TryGetValue(p, out var f) ? f : null);

        var paths = result.Select(r => r.Path).ToList();
        Assert.Single(paths, p => p == "/mods/root/root.pgproj");
        Assert.Single(paths, p => p == "/mods/a/a.pgproj");
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Build_MissingReference_SkipsAndWarns()
    {
        var root = Project("../missing/missing.pgproj");

        var logger = new ListLogger();
        var graph = new ProjectDependencyGraph(logger);
        var result = graph.Build("/mods/root/root.pgproj", root, _ => null);

        var paths = result.Select(r => r.Path).ToList();
        Assert.Single(paths);
        Assert.Equal("/mods/root/root.pgproj", paths[0]);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    private static ILogger<ProjectDependencyGraph> NullLoggerFactory()
    {
        return NullLogger<ProjectDependencyGraph>.Instance;
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class ListLogger : ILogger<ProjectDependencyGraph>
    {
        public ConcurrentBag<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }
}