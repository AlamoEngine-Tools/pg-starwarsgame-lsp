// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

public sealed class ModProjectLoaderTest
{
    private const string ProjectPath = "/workspace/mymod.pgproj";

    [Fact]
    public void Load_Name_ComesFromTopLevelField_IndependentOfModinfo()
    {
        const string json = """
                            {
                              "name": "Top Level Name",
                              "modinfo": { "name": "Modinfo Name" }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("Top Level Name", model.Name);
        Assert.Equal("Modinfo Name", model.Modinfo!.Name);
    }

    [Fact]
    public void Load_Name_FallsBackToFilename_NotModinfoName()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Modinfo Name" }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("mymod", model.Name);
        Assert.Equal("Modinfo Name", model.Modinfo!.Name);
    }

    [Fact]
    public void Load_MissingModinfo_ModinfoIsNull()
    {
        const string json = """
                            {
                              "directories": { "xml": ["data/xml"] }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Null(model.Modinfo);
    }

    [Fact]
    public void Load_InvalidModinfo_ModinfoIsNull()
    {
        const string json = """
                            {
                              "modinfo": "not-an-object"
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Null(model.Modinfo);
    }

    [Fact]
    public void Load_ValidFile_PopulatesAllFields()
    {
        const string json = """
                            {
                              "modinfo": {
                                "name": "My Awesome Mod",
                                "version": "1.0.0",
                                "summary": "Brief description",
                                "icon": "icon.png",
                                "languages": [{ "code": "en", "support": 7 }],
                                "custom": {}
                              },
                              "directories": {
                                "xml":     ["data/xml"],
                                "scripts": ["data/scripts"]
                              },
                              "projectReferences": [
                                { "path": "../basemod/basemod.pgproj" }
                              ]
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("My Awesome Mod", model.Modinfo.Name);
        Assert.Equal("1.0.0", model.Modinfo.Version);
        Assert.Equal(new[] { "data/xml" }, model.Directories.Xml);
        Assert.Equal(new[] { "data/scripts" }, model.Directories.Scripts);
        Assert.Single(model.ProjectReferences);
    }

    [Fact]
    public void Load_MissingModinfo_NameIsFilenameAndModinfoIsNull()
    {
        const string json = """
                            {
                              "directories": { "xml": ["data/xml"] }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("mymod", model.Name);
        Assert.Null(model.Modinfo);
        Assert.NotEmpty(model.Directories.Xml);
    }

    [Fact]
    public void Load_ModinfoMissingName_ProjectNameIsFilename()
    {
        const string json = """
                            {
                              "modinfo": { "version": "1.0.0" },
                              "directories": { "xml": ["data/xml"] }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("mymod", model.Name);
    }

    [Fact]
    public void Load_ModinfoInvalidJson_LogsWarningModinfoIsNullNameIsFilename()
    {
        const string json = """
                            {
                              "modinfo": "not-an-object",
                              "directories": { "xml": ["data/xml"] }
                            }
                            """;
        var loader = Build(json, out var logger);

        var model = loader.Load(ProjectPath);

        Assert.Equal("mymod", model.Name);
        Assert.Null(model.Modinfo);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Load_TopLevelName_UsedAsProjectName()
    {
        const string json = """
                            {
                              "name": "My EaW Mod",
                              "directories": { "xml": ["data/xml"] }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("My EaW Mod", model.Name);
        Assert.Null(model.Modinfo);
    }

    [Fact]
    public void Load_TopLevelName_IndependentOfModinfoName()
    {
        const string json = """
                            {
                              "name": "Project Name",
                              "modinfo": { "name": "Modinfo Name" }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("Project Name", model.Name);
        Assert.Equal("Modinfo Name", model.Modinfo!.Name);
    }

    [Fact]
    public void Load_UnknownTopLevelKeys_Ignored()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "somethingExtra": 42,
                              "anotherThing": { "nested": true }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("Mod", model.Modinfo.Name);
    }

    [Fact]
    public void Load_CommentsAndTrailingCommas_Allowed()
    {
        const string json = """
                            {
                              // this is a comment
                              "modinfo": { "name": "Mod", },
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("Mod", model.Modinfo.Name);
    }

    [Fact]
    public void Load_EmptyProjectReferences_NoWarning()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "projectReferences": []
                            }
                            """;
        var loader = Build(json, out var logger);

        var model = loader.Load(ProjectPath);

        Assert.Empty(model.ProjectReferences);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Load_MultiEntryDirectoryLists_AllPreserved()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "directories": {
                                "xml": ["data/xml", "extra/xml"]
                              }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal(new[] { "data/xml", "extra/xml" }, model.Directories.Xml);
    }

    [Fact]
    public void Load_AbsoluteProjectReference_LogsWarning()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "projectReferences": [
                                { "path": "C:\\BaseMod\\BaseMod.pgproj" }
                              ]
                            }
                            """;
        var loader = Build(json, out var logger);

        loader.Load(ProjectPath);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Load_CustomField_ParsedAsDictionary()
    {
        // Verifies that the reference parser (AlamoEngineTools.Modinfo) is used:
        // System.Text.Json's hand-rolled modinfo DTO stores Custom as JsonElement,
        // but the reference parser stores it as IDictionary<string,object>.
        const string json = """
                            {
                              "modinfo": {
                                "name": "Mod",
                                "custom": { "steamId": "12345", "rating": 5 }
                              }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        var custom = Assert.IsAssignableFrom<IDictionary<string, object>>(model.Modinfo.Custom);
        Assert.True(custom.ContainsKey("steamId"));
    }

    [Fact]
    public void Load_ParsesTextResourceType()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "directories": {
                                "text": ["data/text"],
                                "textResourceType": "dat"
                              }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("dat", model.Directories.TextResourceType);
    }

    [Fact]
    public void Load_AbsentTextResourceType_IsNull()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "directories": { "text": ["data/text"] }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Null(model.Directories.TextResourceType);
    }

    [Fact]
    public void Load_MixedCasePaths_NormalizedToLowercaseForwardSlashes()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "directories": {
                                "xml": ["Data/XML"]
                              },
                              "projectReferences": [
                                { "path": "..\\BaseMod\\BaseMod.pgproj" }
                              ]
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal(new[] { "data/xml" }, model.Directories.Xml);
        Assert.Equal("../basemod/basemod.pgproj", model.ProjectReferences[0].Path);
    }

    private static ModProjectLoader Build(string json, out ListLogger logger)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectPath] = new(json)
        });
        logger = new ListLogger();
        return new ModProjectLoader(new FileHelper(fs), logger);
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class ListLogger : ILogger<ModProjectLoader>
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