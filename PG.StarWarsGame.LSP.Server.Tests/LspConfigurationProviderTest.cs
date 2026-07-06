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
        catch
        {
        }
    }

    private static JsonElement Json(object obj)
    {
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(obj));
    }

    // ── null / default ───────────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_Null_UsesDefaults()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(null);

        Assert.Null(provider.Current.GamePath);
        Assert.Equal("en", provider.Current.Locale);
    }

    // ── init options extraction ──────────────────────────────────────────────

    [Fact]
    public void LoadFrom_WithWorkspaceRoot_PopulatesWorkspaceRoot()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { workspaceRoot = "/mod/eaw" }));
        Assert.Equal("/mod/eaw", provider.Current.WorkspaceRoot);
    }

    [Fact]
    public void LoadFrom_WithGamePath_Extracted()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { baseGamePath = "/game" }));
        Assert.Equal("/game", provider.Current.GamePath);
    }

    [Fact]
    public void LoadFrom_WithLocale_Extracted()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { locale = "de" }));
        Assert.Equal("de", provider.Current.Locale);
    }

    [Fact]
    public void LoadFrom_WithSchemaUrl_OverridesDefault()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { schemaUrl = "http://my-schema/" }));
        Assert.Equal("http://my-schema/", provider.Current.SchemaSource.Url);
    }

    [Fact]
    public void LoadFrom_WithSchemaLocalPath_SetsLocalType()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { schemaLocalPath = "/path/to/schema" }));
        Assert.Equal(SchemaSourceType.Local, provider.Current.SchemaSource.Type);
        Assert.Equal("/path/to/schema", provider.Current.SchemaSource.LocalPath);
    }

    [Fact]
    public void LoadFrom_WithBaselineLocalPath_SetsLocalType()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { baselineLocalPath = "/baselines/eaw.json" }));
        Assert.Equal(BaselineSourceType.Local, provider.Current.BaselineSource.Type);
        Assert.Equal("/baselines/eaw.json", provider.Current.BaselineSource.LocalPath);
    }

    [Fact]
    public void LoadFrom_WithBaselineTypeNone_SetsNoneType()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { baselineType = "None" }));
        Assert.Equal(BaselineSourceType.None, provider.Current.BaselineSource.Type);
    }

    [Fact]
    public void LoadFrom_WithNonJsonElementWhoseToStringIsJson_ParsesCorrectly()
    {
        // Simulate OmniSharp delivering initializationOptions as a Newtonsoft JToken.
        // JToken.ToString() returns the raw JSON string, so our fallback path handles it.
        var fakeToken = new FakeJsonToken("""{"baselineType":"None","schemaLocalPath":"/schema"}""");
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(fakeToken);
        Assert.Equal(BaselineSourceType.None, provider.Current.BaselineSource.Type);
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

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir }));

        Assert.Equal("/from-file", provider.Current.GamePath);
        Assert.Equal("fr", provider.Current.Locale);
    }

    [Fact]
    public void LoadFrom_ConfigFileMissing_UsesDefaults()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        var ex = Record.Exception(() =>
            provider.LoadFrom(Json(new { workspaceRoot = Path.Combine(_tempDir, "no-such-dir") })));

        Assert.Null(ex);
        Assert.Null(provider.Current.GamePath);
    }

    [Fact]
    public void LoadFrom_ConfigFileInvalidJson_UsesDefaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".pg-lsp.json"), "{ not valid json }}}");

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
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

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir, baseGamePath = "overlay-value" }));

        Assert.Equal("overlay-value", provider.Current.GamePath);
    }

    [Fact]
    public void Merge_LocaleDefaultEnDoesNotOverrideFileLocale()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            JsonSerializer.Serialize(new { Locale = "de" }));

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        // No locale in init options → overlay defaults to "en" → should not override file's "de"
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir }));

        Assert.Equal("de", provider.Current.Locale);
    }

    // ── feature flags ────────────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_NoFeaturesNode_AllFeatureFlagsDefaultTrue()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { locale = "en" }));

        var features = provider.Current.Features;
        Assert.True(features.Xml.Completion);
        Assert.True(features.Xml.Hover);
        Assert.True(features.Xml.Diagnostics);
        Assert.True(features.Xml.GoToDefinition);
        Assert.True(features.Xml.FindReferences);
        Assert.True(features.Xml.Rename);
        Assert.True(features.Xml.CodeLens);
        Assert.True(features.Xml.InlayHints);
        Assert.True(features.Xml.CodeActions);
        Assert.True(features.Xml.LinkedEditing);
        Assert.True(features.Lua.Completion);
        Assert.True(features.Lua.Hover);
        Assert.True(features.Lua.Diagnostics);
        Assert.True(features.Lua.GoToDefinition);
        Assert.True(features.Lua.Rename);
        Assert.True(features.Lua.CodeLens);
        Assert.True(features.Lua.InlayHints);
        Assert.True(features.Lua.CodeActions);
        Assert.True(features.Tools.Localisation);
        Assert.True(features.Tools.Variants);
    }

    [Fact]
    public void LoadFrom_PartialFeaturesNode_DisablesLeafKeepsSiblings()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { features = new { xml = new { completion = false } } }));

        Assert.False(provider.Current.Features.Xml.Completion);
        Assert.True(provider.Current.Features.Xml.Hover);
        Assert.True(provider.Current.Features.Lua.Completion);
        Assert.True(provider.Current.Features.Tools.Localisation);
    }

    [Fact]
    public void LoadFrom_FeaturesCamelCaseKeys_MapToPascalCaseProperties()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new
        {
            features = new
            {
                xml = new { goToDefinition = false, inlayHints = false, linkedEditing = false },
                lua = new { codeLens = false, hover = false },
                tools = new { localisation = false }
            }
        }));

        var features = provider.Current.Features;
        Assert.False(features.Xml.GoToDefinition);
        Assert.False(features.Xml.InlayHints);
        Assert.False(features.Xml.LinkedEditing);
        Assert.False(features.Lua.CodeLens);
        Assert.False(features.Lua.Hover);
        Assert.False(features.Tools.Localisation);
        // Untouched leaves keep their defaults.
        Assert.True(features.Xml.Completion);
        Assert.True(features.Lua.Rename);
        Assert.True(features.Tools.Variants);
    }

    [Fact]
    public void LoadFrom_FeaturesViaNonJsonElementToken_Parse()
    {
        var fakeToken = new FakeJsonToken("""{"features":{"lua":{"diagnostics":false}}}""");
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(fakeToken);

        Assert.False(provider.Current.Features.Lua.Diagnostics);
        Assert.True(provider.Current.Features.Lua.Completion);
    }

    [Fact]
    public void LoadFrom_MalformedFeaturesNode_UsesDefaultsWithoutThrowing()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        var ex = Record.Exception(() =>
            provider.LoadFrom(Json(new { features = new { xml = new { completion = "not-a-bool" } } })));

        Assert.Null(ex);
        Assert.True(provider.Current.Features.Xml.Completion);
    }

    [Fact]
    public void Merge_InitOptionsFeaturesWinWholesaleOverFileFeatures()
    {
        // File disables Xml.Hover and Lua.CodeLens; init options send a features node that
        // only disables Xml.Completion. The init options node replaces the file node wholesale
        // (the client always sends the complete resolved object), so the file's flags are gone.
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            """{"Features":{"Xml":{"Hover":false},"Lua":{"CodeLens":false}}}""");

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new
        {
            workspaceRoot = _tempDir,
            features = new { xml = new { completion = false } }
        }));

        Assert.False(provider.Current.Features.Xml.Completion);
        Assert.True(provider.Current.Features.Xml.Hover);
        Assert.True(provider.Current.Features.Lua.CodeLens);
    }

    [Fact]
    public void Merge_NoFeaturesInInitOptions_FileFeaturesApply()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            """{"Features":{"Xml":{"Hover":false}}}""");

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir }));

        Assert.False(provider.Current.Features.Xml.Hover);
        Assert.True(provider.Current.Features.Xml.Completion);
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
        var provider = new LspConfigurationProvider(fs, NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { workspaceRoot = MockRoot }));

        Assert.Equal("/mock-path", provider.Current.GamePath);
        Assert.Equal("de", provider.Current.Locale);
    }

    [Fact]
    public void LoadConfigFile_FileAbsentInMock_UsesDefaults()
    {
        var fs = new MockFileSystem();
        var provider = new LspConfigurationProvider(fs, NullLogger<LspConfigurationProvider>.Instance);
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