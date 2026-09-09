// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

public sealed class ModProjectLoaderTest
{
    private const string ProjectPath = "/workspace/mymod.pgproj";

    // ── the format contract ──────────────────────────────────────────────────

    // Every .pgproj written so far predates the field, so its absence is the ordinary case.
    [Fact]
    public void Load_NoIdentityFields_IsTheCurrentFormat()
    {
        var loader = Build("""{ "name": "Mod" }""", out _);

        Assert.Equal("Mod", loader.Load(ProjectPath).Name);
    }

    [Fact]
    public void Load_CurrentIdentity_Loads()
    {
        var loader = Build(
            $$"""{ "_type": "{{PgprojFormat.TypeName}}", "_typeVersion": "{{PgprojFormat.Current}}", "name": "Mod" }""",
            out _);

        Assert.Equal("Mod", loader.Load(ProjectPath).Name);
    }

    // Enforced upgrading, hard: a project from a newer extension is refused rather than read as
    // best we can, because the next thing this build would do is write its own shape back over it.
    [Fact]
    public void Load_NewerTypeVersion_RefusesWithAMessageNamingTheVersion()
    {
        var loader = Build("""{ "name": "Mod", "_typeVersion": "aetswg-99.0.0" }""", out _);

        var error = Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));

        Assert.Contains("99.0.0", error.Message, StringComparison.Ordinal);
        Assert.Contains("mymod.pgproj", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_UnreadableTypeVersion_Refuses()
    {
        var loader = Build("""{ "name": "Mod", "_typeVersion": "banana" }""", out _);

        Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));
    }

    // ── migrating on read ────────────────────────────────────────────────────

    // The project is brought forward in memory every time it is read, whether or not anybody ever
    // agrees to write it back. Reading it as its old shape would mean the rest of the server sees
    // a project that no longer exists.
    [Fact]
    public void Load_OlderVersion_IsMigratedInMemory()
    {
        var loader = BuildWith(
            """{ "_typeVersion": "aetswg-0.9.0", "renamedName": "From The Past" }""", out _,
            migrations: new RenameToName());

        Assert.Equal("From The Past", loader.Load(ProjectPath).Name);
    }

    // ...and the file itself is not touched by reading it. Writing is a separate, consented act.
    [Fact]
    public void Load_OlderVersion_LeavesTheFileAlone()
    {
        const string original = """{ "_typeVersion": "aetswg-0.9.0", "renamedName": "From The Past" }""";
        var loader = BuildWith(original, out var fs, migrations: new RenameToName());

        loader.Load(ProjectPath);

        Assert.Equal(original, fs.File.ReadAllText(ProjectPath));
    }

    [Fact]
    public void Load_OlderVersion_TellsTheSinkWithTheMigratedDocumentAndItsNotices()
    {
        var sink = new RecordingSink();
        var loader = BuildWith(
            """{ "_typeVersion": "aetswg-0.9.0", "renamedName": "From The Past" }""", out _, sink,
            new RenameToName());

        loader.Load(ProjectPath);

        var (path, migrated, notices) = Assert.Single(sink.Calls);
        Assert.Equal(ProjectPath, path);
        Assert.Equal("From The Past", (string?)migrated["name"]);
        Assert.Equal(PgprojFormat.Current.ToString(), (string?)migrated["_typeVersion"]);
        Assert.Equal("The 'renamedName' field is called 'name' now.", Assert.Single(notices));
    }

    [Fact]
    public void Load_CurrentVersion_TellsTheSinkNothing()
    {
        var sink = new RecordingSink();
        var loader = BuildWith(
            $$"""{ "_typeVersion": "{{PgprojFormat.Current}}", "name": "Mod" }""", out _, sink,
            new RenameToName());

        loader.Load(ProjectPath);

        Assert.Empty(sink.Calls);
    }

    // A gap in the chain is a bug in our registration. Loading the project at a version whose shape
    // it does not have would paper over it - and this file is the one everything else is read from.
    [Fact]
    public void Load_OlderVersionWithNoMigrationForIt_Refuses()
    {
        var loader = BuildWith(
            """{ "_typeVersion": "aetswg-0.1.0", "name": "Mod" }""", out _, migrations: new RenameToName());

        Assert.Throws<ModProjectLoadException>(() => loader.Load(ProjectPath));
    }

    // ── the initial typing of the project file ───────────────────────────────

    // Every .pgproj in existence predates the identity fields, so the first thing this framework
    // owes them is the migration that gives them one. Without it the fields only ever appear on
    // projects we happen to create or rewrite, and the format contract describes nothing.
    [Fact]
    public void Load_ExistingUnstampedProject_IsOfferedItsIdentity()
    {
        var sink = new RecordingSink();
        var loader = BuildWith("""{ "name": "Mod" }""", out _, sink, PgprojMigrations.All.ToArray());

        loader.Load(ProjectPath);

        var (_, migrated, _) = Assert.Single(sink.Calls);
        Assert.Equal(PgprojFormat.TypeName, (string?)migrated["_type"]);
        Assert.Equal(PgprojFormat.Current.ToString(), (string?)migrated["_typeVersion"]);
    }

    // The initial migration adds an identity and changes NOTHING else - it exists because the
    // document became persistent storage, not because its shape moved.
    [Fact]
    public void Load_ExistingUnstampedProject_IsOtherwiseUntouched()
    {
        var sink = new RecordingSink();
        var loader = BuildWith(
            """{ "name": "Mod", "directories": { "xml": ["data/xml"] }, "unknownToUs": 42 }""",
            out _, sink, PgprojMigrations.All.ToArray());

        loader.Load(ProjectPath);

        var (_, migrated, _) = Assert.Single(sink.Calls);
        Assert.Equal("Mod", (string?)migrated["name"]);
        Assert.Equal("data/xml", (string?)migrated["directories"]!["xml"]![0]);
        // A key this build does not model survives, because the migration runs over the raw tree.
        Assert.Equal(42, (int?)migrated["unknownToUs"]);
    }

    // Reading it does not write it: the identity is proposed, and the user decides.
    [Fact]
    public void Load_ExistingUnstampedProject_LeavesTheFileAlone()
    {
        const string original = """{ "name": "Mod" }""";
        var loader = BuildWith(original, out var fs, null, PgprojMigrations.All.ToArray());

        loader.Load(ProjectPath);

        Assert.Equal(original, fs.File.ReadAllText(ProjectPath));
    }

    // A project already carrying its identity has nothing to be offered.
    [Fact]
    public void Load_StampedProject_IsNotOfferedAnything()
    {
        var sink = new RecordingSink();
        var loader = BuildWith(
            $$"""{ "_type": "{{PgprojFormat.TypeName}}", "_typeVersion": "{{PgprojFormat.Current}}", "name": "Mod" }""",
            out _, sink, PgprojMigrations.All.ToArray());

        loader.Load(ProjectPath);

        Assert.Empty(sink.Calls);
    }

    /// <summary>A fixture step: version 0.9.0 called the project's name something else.</summary>
    private sealed class RenameToName : IDocumentMigration
    {
        public string TypeName => PgprojFormat.TypeName;
        public TypeVersion From => TypeVersion.Of("aetswg", 0, 9);
        public TypeVersion To => PgprojFormat.Current;
        public string? UserNotice => "The 'renamedName' field is called 'name' now.";

        public JsonNode Migrate(JsonNode document)
        {
            var root = document.AsObject();
            if (root["renamedName"] is { } renamed)
            {
                root.Remove("renamedName");
                root["name"] = renamed.DeepClone();
            }

            return root;
        }
    }

    private sealed class RecordingSink : IPgprojMigrationSink
    {
        public List<(string Path, JsonObject Migrated, IReadOnlyList<string> Notices)> Calls { get; } = [];

        public void Migrated(string pgprojPath, JsonNode migrated, IReadOnlyList<string> notices)
        {
            Calls.Add((pgprojPath, migrated.AsObject(), notices));
        }
    }

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

        Assert.Equal("My Awesome Mod", model.Modinfo!.Name);
        Assert.Equal("1.0.0", model.Modinfo!.Version);
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

        Assert.Equal(["Data/Scripts/Story"], model.Directories.StoryDialog);
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

        Assert.Equal("Mod", model.Modinfo!.Name);
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

        Assert.Equal("Mod", model.Modinfo!.Name);
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

        var custom = Assert.IsAssignableFrom<IDictionary<string, object>>(model.Modinfo!.Custom);
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
    public void Load_LocalisationNode_DirectoryNormalizedToForwardSlashes_KeepingItsCase()
    {
        const string json = """
                            {
                              "modinfo": { "name": "Mod" },
                              "localisation": { "type": "CSV", "directory": "Data\\Text" }
                            }
                            """;
        var loader = Build(json, out _);

        var model = loader.Load(ProjectPath);

        Assert.Equal("Data/Text", model.Localisation!.Directory);
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

    // Separators are unified; case is not. These paths are opened, not just compared - the loader
    // used to lowercase them, which on a case-sensitive host turns an author's working project
    // into one whose icon roots cannot be found. Callers that need to MATCH two of these compare
    // them through DocumentUris instead.
    [Fact]
    public void Load_IconsNode_PathsNormalizedToForwardSlashes_KeepingTheirCase()
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

        Assert.Equal("Data/Art/Textures/MT_MyMod", model.Icons!.MegaTexture);
        Assert.Equal("Data/Art/Textures/MT_MyMod.mtd", model.Icons.MtdPath);
        Assert.Equal(["Data/Art/Icons"], model.Icons.SourceRoots);
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
    public void Load_MixedCasePaths_NormalizedToForwardSlashes_KeepingTheirCase()
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

        Assert.Equal(new[] { "Data/XML" }, model.Directories.Xml);
        Assert.Equal("../BaseMod/BaseMod.pgproj", model.ProjectReferences[0].Path);
    }

    private static ModProjectLoader Build(string json, out ListLogger logger)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectPath] = new(json)
        });
        logger = new ListLogger();
        return new ModProjectLoader(
            new FileHelper(fs), logger, new NullPgprojMigrationSink(), PgprojMigrations.All);
    }

    /// <summary>A loader with a chain and a sink, for the migrate-on-read tests.</summary>
    private static ModProjectLoader BuildWith(
        string json, out MockFileSystem fs, IPgprojMigrationSink? sink = null,
        params IDocumentMigration[] migrations)
    {
        fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectPath] = new(json)
        });
        return new ModProjectLoader(
            new FileHelper(fs), new ListLogger(), sink ?? new NullPgprojMigrationSink(), migrations);
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