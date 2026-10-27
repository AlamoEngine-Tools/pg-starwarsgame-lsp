// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class LspConfigurationProviderTest : IDisposable
{
    private static readonly string MockRoot =
        Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, "test-workspace");

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public LspConfigurationProviderTest()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch (IOException)
        {
            // A temp directory that cannot be deleted is not a test failure.
        }
    }

    private static JsonElement Json(object obj)
    {
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(obj));
    }

    // ── game installs ────────────────────────────────────────────────────────

    private sealed class FakeInstalls(string? baseGame, string? expansion) : IGameInstallLocator
    {
        public int Calls { get; private set; }

        public string? FindBaseGame()
        {
            Calls++;
            return baseGame;
        }

        public string? FindExpansion()
        {
            Calls++;
            return expansion;
        }
    }

    [Fact]
    public void LoadFrom_NoGamePaths_TakesTheDetectedInstalls()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls("C:/Steam/Empire at War/GameData", "C:/Steam/Empire at War/corruption"));

        provider.LoadFrom(Json(new { workspaceRoot = "/mod/eaw" }));

        Assert.Equal("C:/Steam/Empire at War/GameData", provider.Current.GamePath);
        Assert.Equal("C:/Steam/Empire at War/corruption", provider.Current.ExpansionPath);
    }

    [Fact]
    public void LoadFrom_ASetGamePath_WinsOverTheDetectedOne()
    {
        var installs = new FakeInstalls("C:/detected/GameData", "C:/detected/corruption");
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            installs);

        provider.LoadFrom(Json(new { baseGamePath = "D:/eaw", expansionGamePath = "D:/foc" }));

        Assert.Equal("D:/eaw", provider.Current.GamePath);
        Assert.Equal("D:/foc", provider.Current.ExpansionPath);
        Assert.Equal(0, installs.Calls);
    }

    [Fact]
    public void LoadFrom_NothingDetected_LeavesTheGamePathsUnset()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));

        provider.LoadFrom(null);

        Assert.Null(provider.Current.GamePath);
        Assert.Null(provider.Current.ExpansionPath);
    }

    // ── null / default ───────────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_Null_UsesDefaults()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(null);

        Assert.Null(provider.Current.GamePath);
        Assert.Equal("en", provider.Current.Locale);
    }

    // ── init options extraction ──────────────────────────────────────────────

    [Fact]
    public void LoadFrom_WithWorkspaceRoot_PopulatesWorkspaceRoot()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { workspaceRoot = "/mod/eaw" }));
        Assert.Equal("/mod/eaw", provider.Current.WorkspaceRoot);
    }

    /// <summary>
    ///     One server per open project: the client names the project file to load instead of
    ///     leaving the server to find one under the folders.
    /// </summary>
    [Fact]
    public void LoadFrom_WithProjectPath_PopulatesProjectPath()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { workspaceRoot = "/mod/eaw", projectPath = "/mod/eaw/eaw.pgproj" }));
        Assert.Equal("/mod/eaw/eaw.pgproj", provider.Current.ProjectPath);
    }

    [Fact]
    public void LoadFrom_WithoutProjectPath_LeavesItNull()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { workspaceRoot = "/mod/eaw" }));
        Assert.Null(provider.Current.ProjectPath);
    }

    /// <summary>The client offers a project file for a mod folder without one, and says so (#295).</summary>
    [Fact]
    public void LoadFrom_ProjectFileOffered_IsKept_AndDefaultsToFalse()
    {
        var offered = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        var plain = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));

        offered.LoadFrom(Json(new { workspaceRoot = "/mod/eaw", projectFileOffered = true }));
        plain.LoadFrom(Json(new { workspaceRoot = "/mod/eaw" }));

        Assert.True(offered.Current.ProjectFileOffered);
        Assert.False(plain.Current.ProjectFileOffered);
    }

    [Fact]
    public void LoadFrom_WithGamePath_Extracted()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { baseGamePath = "/game" }));
        Assert.Equal("/game", provider.Current.GamePath);
    }

    [Fact]
    public void LoadFrom_WithLocale_Extracted()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { locale = "de" }));
        Assert.Equal("de", provider.Current.Locale);
    }

    [Fact]
    public void LoadFrom_WithSchemaUrl_OverridesDefault()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { schemaUrl = "http://my-schema/" }));
        Assert.Equal("http://my-schema/", provider.Current.SchemaSource.Url);
    }

    [Fact]
    public void LoadFrom_WithSchemaLocalPath_SetsLocalType()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { schemaLocalPath = "/path/to/schema" }));
        Assert.Equal(SchemaSourceType.Local, provider.Current.SchemaSource.Type);
        Assert.Equal("/path/to/schema", provider.Current.SchemaSource.LocalPath);
    }

    [Fact]
    public void LoadFrom_WithBaselineLocalPath_SetsLocalType()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { baselineLocalPath = "/baselines/eaw.json" }));
        Assert.Equal(BaselineSourceType.Local, provider.Current.BaselineSource.Type);
        Assert.Equal("/baselines/eaw.json", provider.Current.BaselineSource.LocalPath);
    }

    [Fact]
    public void LoadFrom_WithBaselineTypeNone_IsNoLongerASource()
    {
        // "none" was an escape hatch for running without the shipped-game index. Inside a mod
        // project that is not a mode, it is a broken setup: every cross-reference diagnostic would
        // report objects the game defines as missing. The value is gone from the settings enum, so a
        // stale one falls through to the ordinary precedence rather than being honoured.
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { baselineType = "None" }));
        Assert.Equal(BaselineSourceType.Http, provider.Current.BaselineSource.Type);
    }

    [Fact]
    public void LoadFrom_WithBaselineTypeNoneAndLocalPath_StillPrefersTheLocalPath()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { baselineType = "None", baselineLocalPath = "/baselines/eaw.aet" }));
        Assert.Equal(BaselineSourceType.Local, provider.Current.BaselineSource.Type);
        Assert.Equal("/baselines/eaw.aet", provider.Current.BaselineSource.LocalPath);
    }

    [Fact]
    public void LoadFrom_WithNonJsonElementWhoseToStringIsJson_ParsesCorrectly()
    {
        // Simulate OmniSharp delivering initializationOptions as a Newtonsoft JToken.
        // JToken.ToString() returns the raw JSON string, so our fallback path handles it.
        var fakeToken = new FakeJsonToken("""{"baselineLocalPath":"/baselines/eaw.aet","schemaLocalPath":"/schema"}""");
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(fakeToken);
        Assert.Equal(BaselineSourceType.Local, provider.Current.BaselineSource.Type);
        Assert.Equal(SchemaSourceType.Local, provider.Current.SchemaSource.Type);
    }

    // ── workspace config file ────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_WithWorkspaceRoot_LoadsConfigFile()
    {
        var config = new { GamePath = "/from-file", Locale = "fr" };
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            JsonSerializer.Serialize(config));

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir }));

        Assert.Equal("/from-file", provider.Current.GamePath);
        Assert.Equal("fr", provider.Current.Locale);
    }

    /// <summary>
    ///     The merge used to rebuild <see cref="LspConfiguration" /> property by property and simply
    ///     omit <c>Localisation</c>, so every localisation setting written into <c>.pg-lsp.json</c> was
    ///     silently replaced by the defaults the moment initializationOptions were overlaid.
    /// </summary>
    [Fact]
    public void LoadFrom_ConfigFileDeclaresLocalisation_SurvivesTheOverlay()
    {
        var config = new
        {
            Localisation = new { ResourceType = "Xml", Language = "GERMAN", SourcePaths = new[] { "/a/b.xml" } }
        };
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            JsonSerializer.Serialize(config));

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir }));

        Assert.Equal("Xml", provider.Current.Localisation.ResourceType);
        Assert.Equal("GERMAN", provider.Current.Localisation.Language);
        Assert.Equal(["/a/b.xml"], provider.Current.Localisation.SourcePaths);
    }

    [Fact]
    public void LoadFrom_ConfigFileMissing_UsesDefaults()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        var ex = Record.Exception(() =>
            provider.LoadFrom(Json(new { workspaceRoot = Path.Combine(_tempDir, "no-such-dir") })));

        Assert.Null(ex);
        Assert.Null(provider.Current.GamePath);
    }

    [Fact]
    public void LoadFrom_ConfigFileInvalidJson_UsesDefaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".pg-lsp.json"), "{ not valid json }}}");

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        var ex = Record.Exception(() => provider.LoadFrom(Json(new { workspaceRoot = _tempDir })));

        Assert.Null(ex);
    }

    // ── merge semantics ──────────────────────────────────────────────────────

    [Fact]
    public void Merge_InitOptionsOverrideFileGamePath()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            JsonSerializer.Serialize(new { GamePath = "file-value" }));

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir, baseGamePath = "overlay-value" }));

        Assert.Equal("overlay-value", provider.Current.GamePath);
    }

    [Fact]
    public void Merge_LocaleDefaultEnDoesNotOverrideFileLocale()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            JsonSerializer.Serialize(new { Locale = "de" }));

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        // No locale in init options → overlay defaults to "en" → should not override file's "de"
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir }));

        Assert.Equal("de", provider.Current.Locale);
    }

    // ── diagnostics ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Every key in initializationOptions is read by NAME, so a node nobody parses is dropped in
    ///     silence. That is how the workspace-sweep setting first shipped doing nothing: its unit
    ///     tests passed, the client sent the key, and the server never looked at it.
    /// </summary>
    [Fact]
    public void LoadFrom_DiagnosticsNode_IsRead()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { diagnostics = new { workspaceOnStartup = true } }));

        Assert.True(provider.Current.Diagnostics.WorkspaceOnStartup);
    }

    [Fact]
    public void LoadFrom_NoDiagnosticsNode_WorkspaceSweepIsOff()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { locale = "en" }));

        Assert.False(provider.Current.Diagnostics.WorkspaceOnStartup);
    }

    [Fact]
    public void LoadFrom_DiagnosticsNodeFalse_StaysOff()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { diagnostics = new { workspaceOnStartup = false } }));

        Assert.False(provider.Current.Diagnostics.WorkspaceOnStartup);
    }

    // ── feature flags ────────────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_NoFeaturesNode_BothFlagsDefaultTrue()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { locale = "en" }));

        Assert.True(provider.Current.Features.StoryEditing);
        Assert.True(provider.Current.Features.StorySimulator);
    }

    [Fact]
    public void LoadFrom_StoryEditingDisabled_ParsesFromCamelCaseNode()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { features = new { storyEditing = false } }));

        Assert.False(provider.Current.Features.StoryEditing);
        // The untouched flag keeps its default.
        Assert.True(provider.Current.Features.StorySimulator);
    }

    [Fact]
    public void LoadFrom_StorySimulatorDisabled_ParsesFromCamelCaseNode()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { features = new { storySimulator = false } }));

        Assert.False(provider.Current.Features.StorySimulator);
        Assert.True(provider.Current.Features.StoryEditing);
    }

    [Fact]
    public void LoadFrom_FeaturesViaNonJsonElementToken_Parse()
    {
        var fakeToken = new FakeJsonToken("""{"features":{"storySimulator":false}}""");
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(fakeToken);

        Assert.False(provider.Current.Features.StorySimulator);
        Assert.True(provider.Current.Features.StoryEditing);
    }

    [Fact]
    public void LoadFrom_MalformedFeaturesNode_UsesDefaultsWithoutThrowing()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        var ex = Record.Exception(() =>
            provider.LoadFrom(Json(new { features = new { storyEditing = "not-a-bool" } })));

        Assert.Null(ex);
        Assert.True(provider.Current.Features.StoryEditing);
        Assert.True(provider.Current.Features.StorySimulator);
    }

    [Fact]
    public void Merge_InitOptionsFeaturesWinWholesaleOverFileFeatures()
    {
        // The file turns the simulator off; init options send a features node that only turns
        // editing off. The init options node replaces the file node wholesale (the client always
        // sends the complete resolved object), so the file's flag is gone.
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            """{"Features":{"StorySimulator":false}}""");

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new
        {
            workspaceRoot = _tempDir,
            features = new { storyEditing = false }
        }));

        Assert.False(provider.Current.Features.StoryEditing);
        Assert.True(provider.Current.Features.StorySimulator);
    }

    [Fact]
    public void Merge_NoFeaturesInInitOptions_FileFeaturesApply()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            """{"Features":{"StoryEditing":false}}""");

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir }));

        Assert.False(provider.Current.Features.StoryEditing);
        Assert.True(provider.Current.Features.StorySimulator);
    }

    // ── MockFileSystem tests ─────────────────────────────────────────────────

    [Fact]
    public void LoadConfigFile_FileExistsInMock_LoadsValues()
    {
        var configJson = JsonSerializer.Serialize(new { GamePath = "/mock-path", Locale = "de" });
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(MockRoot, ".pg-lsp.json")] = new(configJson)
        });
        var provider = new LspConfigurationProvider(fs, NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { workspaceRoot = MockRoot }));

        Assert.Equal("/mock-path", provider.Current.GamePath);
        Assert.Equal("de", provider.Current.Locale);
    }

    [Fact]
    public void LoadConfigFile_FileAbsentInMock_UsesDefaults()
    {
        var fs = new MockFileSystem();
        var provider = new LspConfigurationProvider(fs, NullLogger<LspConfigurationProvider>.Instance,
            new FakeInstalls(null, null));
        provider.LoadFrom(Json(new { workspaceRoot = MockRoot }));

        Assert.Null(provider.Current.GamePath);
    }

    private sealed class FakeJsonToken
    {
        private readonly string _json;

        public FakeJsonToken(string json)
        {
            _json = json;
        }

        public override string ToString()
        {
            return _json;
        }
    }
}