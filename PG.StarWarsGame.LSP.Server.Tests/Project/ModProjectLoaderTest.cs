// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Project;
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
    public void Load_StoryDialogDirectories_ParsedAndNormalized()
    {
        const string json = """
                            {
                              "directories": {
                                "xml": ["data/xml"],
                                "storyDialog": ["Data\\Scripts\\Story"]
                              }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal(["data/scripts/story"], model.Directories.StoryDialog);
    }

    [Fact]
    public void Load_NoStoryDialogNode_YieldsEmptyList()
    {
        const string json = """
                            {
                              "directories": { "xml": ["data/xml"] }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Empty(model.Directories.StoryDialog);
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
    public void Load_ParsesLocalisationNode()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "localisation": { "type": "DAT", "directory": "data/text" }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("DAT", model.Localisation!.Type);
        Assert.Equal("data/text", model.Localisation.Directory);
    }

    [Fact]
    public void Load_LocalisationNode_TypeIsCaseInsensitive_NormalizedToUppercase()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "localisation": { "type": "csv", "directory": "data/text" }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("CSV", model.Localisation!.Type);
    }

    [Fact]
    public void Load_LocalisationNode_DirectoryNormalizedToLowercaseForwardSlashes()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "localisation": { "type": "CSV", "directory": "Data\\Text" }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("data/text", model.Localisation!.Directory);
    }

    [Fact]
    public void Load_AbsentLocalisationNode_IsNull()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "directories": { "xml": ["data/xml"] }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Null(model.Localisation);
    }

    // ── icons ────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_IconsNode_IsParsed()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "icons": {
                                "megaTexture": "data/art/textures/mt_mymod",
                                "sourceRoots": ["data/art/textures/icons"]
                              }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("data/art/textures/mt_mymod", model.Icons!.MegaTexture);
        Assert.Equal("data/art/textures/mt_mymod.mtd", model.Icons.MtdPath);
        Assert.Equal("data/art/textures/mt_mymod.tga", model.Icons.TexturePath);
        Assert.Equal(["data/art/textures/icons"], model.Icons.SourceRoots);
    }

    [Fact]
    public void Load_IconsNode_PathsNormalizedToLowercaseForwardSlashes()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "icons": {
                                "megaTexture": "Data\\Art\\Textures\\MT_MyMod",
                                "sourceRoots": ["Data\\Art\\Icons"]
                              }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("data/art/textures/mt_mymod", model.Icons!.MegaTexture);
        Assert.Equal(["data/art/icons"], model.Icons.SourceRoots);
    }

    // Absence is not "no icons" - the engine always looks in the same place, so the default applies.
    [Fact]
    public void Load_AbsentIconsNode_IsNull_AndDefaultSuppliesTheConvention()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "directories": { "xml": ["data/xml"] }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Null(model.Icons);
        Assert.Equal("data/art/textures/mt_commandbar",
            (model.Icons ?? IconProjectSettings.Default).MegaTexture);
    }

    [Fact]
    public void Load_IconsNode_WithOnlySourceRoots_KeepsConventionalMegaTexture()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "icons": { "sourceRoots": ["data/art/icons"] }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal(IconProjectSettings.ConventionalMegaTexture, model.Icons!.MegaTexture);
        Assert.Equal(["data/art/icons"], model.Icons.SourceRoots);
    }

    // megaTexture names a PAIR, so an extension is ambiguous about which half was meant. Silently
    // stripping it would hide a real misunderstanding of the format.
    [Theory]
    [InlineData("data/art/textures/mt_mymod.mtd")]
    [InlineData("data/art/textures/mt_mymod.tga")]
    [InlineData("data/art/textures/mt_mymod.TGA")]
    public void Load_IconsNode_MegaTextureWithExtension_Throws(string megaTexture)
    {
        var json = $$"""
                     {
                       "modinfo": { "name": "Mod" },
                       "icons": { "megaTexture": "{{megaTexture}}" }
                     }
                     """;
        var loader = Build(json, out _);

        var ex = Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));
        Assert.Contains("without a file extension", ex.Message);
    }

    [Fact]
    public void Load_IconsNode_EmptyMegaTexture_Throws()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "icons": { "megaTexture": "  " }
                            }
                            """;
        var loader = Build(json, out _);

        var ex = Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));
        Assert.Contains("must not be empty", ex.Message);
    }

    [Fact]
    public void Load_IconsNode_EmptySourceRootEntry_Throws()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "icons": { "sourceRoots": ["data/art/icons", ""] }
                            }
                            """;
        var loader = Build(json, out _);

        var ex = Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));
        Assert.Contains("must not contain empty entries", ex.Message);
    }

    // ── localisation.credits ─────────────────────────────────────────────────

    [Fact]
    public void Load_CreditsNode_IsParsed()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "localisation": {
                                "type": "CSV", "directory": "data/text",
                                "credits": { "detection": "explicit", "files": ["rolls.csv"] }
                              }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("explicit", model.Localisation!.Credits!.Detection);
        Assert.Equal(["rolls.csv"], model.Localisation.Credits.Files);
    }

    // Absent means "use the convention", which is the behaviour every existing .pgproj already
    // relies on - it must not become a required node.
    [Fact]
    public void Load_AbsentCreditsNode_IsNull()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "localisation": { "type": "CSV", "directory": "data/text" }
                            }
                            """;
        var loader = Build(json, out _);

        Assert.Null(loader.Load(ProjectPath).Localisation!.Credits);
    }

    [Fact]
    public void Load_CreditsNode_DetectionDefaultsToConvention()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "localisation": {
                                "type": "CSV", "directory": "data/text",
                                "credits": { "files": ["rolls.csv"] }
                              }
                            }
                            """;
        var loader = Build(json, out _);

        Assert.Equal("convention", loader.Load(ProjectPath).Localisation!.Credits!.Detection);
    }

    // "explicit" with nothing listed classifies nothing, which is indistinguishable from "none"
    // except that the author plainly meant to list something. Fail rather than silently do nothing.
    [Fact]
    public void Load_CreditsExplicitWithNoFiles_ThrowsClearException()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "localisation": {
                                "type": "CSV", "directory": "data/text",
                                "credits": { "detection": "explicit" }
                              }
                            }
                            """;
        var loader = Build(json, out _);

        var ex = Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));
        Assert.Contains("localisation.credits.files", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_CreditsUnrecognisedDetection_ThrowsClearException()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "localisation": {
                                "type": "CSV", "directory": "data/text",
                                "credits": { "detection": "guess" }
                              }
                            }
                            """;
        var loader = Build(json, out _);

        var ex = Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));
        Assert.Contains("localisation.credits.detection", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("convention", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_CreditsDetection_IsCaseInsensitive_NormalizedToLowercase()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "localisation": {
                                "type": "CSV", "directory": "data/text",
                                "credits": { "detection": "NONE" }
                              }
                            }
                            """;
        var loader = Build(json, out _);

        Assert.Equal("none", loader.Load(ProjectPath).Localisation!.Credits!.Detection);
    }

    [Fact]
    public void Load_LocalisationNode_UnrecognisedType_ThrowsClearException()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "localisation": { "type": "TXT", "directory": "data/text" }
                            }
                            """;
        var loader = Build(json, out _);

        var ex = Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));
        Assert.Contains("localisation.type", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CSV", ex.Message);
    }

    [Fact]
    public void Load_LocalisationNode_MissingDirectory_ThrowsClearException()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "localisation": { "type": "CSV" }
                            }
                            """;
        var loader = Build(json, out _);

        var ex = Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));
        Assert.Contains("localisation.directory", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_OldDirectoriesTextAndTypeShape_ThrowsClearException()
    {
        // Clean break: the old directories.text/textResourceType shape was removed in favour of
        // the top-level "localisation" node. Silently ignoring it (System.Text.Json's default
        // behaviour for unknown properties) would leave the mod's localisation quietly
        // unconfigured with no indication why - so this must hard-fail with a migration hint
        // instead of loading successfully.
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "directories": { "text": ["data/text"], "textResourceType": "csv" }
                            }
                            """;
        var loader = Build(json, out _);

        var ex = Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));
        Assert.Contains("directories.text", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("localisation", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mymod.pgproj", ex.Message);
    }

    [Fact]
    public void Load_OldDirectoriesTextResourceTypeOnly_ThrowsClearException()
    {
        // The two legacy fields could theoretically appear independently; either one alone must
        // still be caught.
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "directories": { "xml": ["data/xml"], "textResourceType": "csv" }
                            }
                            """;
        var loader = Build(json, out _);

        var ex = Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));
        Assert.Contains("directories.text", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_ProjectReferencesNotAnArray_ThrowsClearExceptionMentioningField()
    {
        // EaWX Revan's Revenge wrote projectReferences as a single object instead of an array.
        // The raw JsonException ("could not be converted to List`1...") is cryptic; the loader must
        // translate it into a clear, actionable message.
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "projectReferences": { "path": "../data/eawx-core.pgproj" }
                            }
                            """;
        var loader = Build(json, out _);

        var ex = Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));
        Assert.Contains("projectReferences", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("array", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mymod.pgproj", ex.Message);
    }

    [Fact]
    public void Load_MalformedJson_ThrowsClearExceptionMentioningFileAndLocation()
    {
        var loader = Build("""
                           {
                             "name": "Mod",
                             "directories": { "xml": [ }
                           }
                           """, out _);

        var ex = Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));
        Assert.Contains("mymod.pgproj", ex.Message);
        Assert.Contains("line", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_ObjectFormProjectReferences_StillSupported()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "projectReferences": [ { "path": "../base/base.pgproj" } ]
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Single(model.ProjectReferences);
        Assert.Equal("../base/base.pgproj", model.ProjectReferences[0].Path);
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