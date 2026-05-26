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
        Assert.Empty(provider.Current.ModPaths);
    }

    // ── init options extraction ──────────────────────────────────────────────

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

    [Fact]
    public void LoadFrom_WithModPaths_Extracted()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { modPaths = new[] { "/mod/a", "/mod/b" } }));
        Assert.Equal(2, provider.Current.ModPaths.Count);
        Assert.Contains("/mod/a", provider.Current.ModPaths);
    }

    [Fact]
    public void LoadFrom_WithXmlDirectories_Extracted()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { xmlDirectories = new[] { "/game/data/xml", "/mod/data/xml" } }));
        Assert.Equal(2, provider.Current.XmlDirectories.Count);
        Assert.Contains("/game/data/xml", provider.Current.XmlDirectories);
        Assert.Contains("/mod/data/xml", provider.Current.XmlDirectories);
    }

    [Fact]
    public void LoadFrom_NoXmlDirectories_DefaultsToEmpty()
    {
        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(null);
        Assert.Empty(provider.Current.XmlDirectories);
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

    [Fact]
    public void Merge_OverlayModPathsOverrideFile()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            JsonSerializer.Serialize(new { ModPaths = new[] { "/file-mod" } }));

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir, modPaths = new[] { "/overlay-mod" } }));

        Assert.Single(provider.Current.ModPaths);
        Assert.Equal("/overlay-mod", provider.Current.ModPaths[0]);
    }

    [Fact]
    public void Merge_EmptyOverlayModPaths_FileValueKept()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            JsonSerializer.Serialize(new { ModPaths = new[] { "/from-file" } }));

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        // No modPaths in init options → overlay has empty list → file's value should be kept
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir }));

        Assert.Single(provider.Current.ModPaths);
        Assert.Equal("/from-file", provider.Current.ModPaths[0]);
    }

    [Fact]
    public void Merge_OverlayXmlDirectoriesOverrideFile()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            JsonSerializer.Serialize(new { XmlDirectories = new[] { "/file-dir" } }));

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir, xmlDirectories = new[] { "/overlay-dir" } }));

        Assert.Single(provider.Current.XmlDirectories);
        Assert.Equal("/overlay-dir", provider.Current.XmlDirectories[0]);
    }

    [Fact]
    public void Merge_EmptyOverlayXmlDirectories_FileValueKept()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            JsonSerializer.Serialize(new { XmlDirectories = new[] { "/from-file" } }));

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir }));

        Assert.Single(provider.Current.XmlDirectories);
        Assert.Equal("/from-file", provider.Current.XmlDirectories[0]);
    }

    [Fact]
    public void LoadFrom_XmlDirectoriesFromConfigFile_Loaded()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, ".pg-lsp.json"),
            JsonSerializer.Serialize(new { XmlDirectories = new[] { "/file-dir-a", "/file-dir-b" } }));

        var provider = new LspConfigurationProvider(new FileSystem(), NullLogger<LspConfigurationProvider>.Instance);
        provider.LoadFrom(Json(new { workspaceRoot = _tempDir }));

        Assert.Equal(2, provider.Current.XmlDirectories.Count);
        Assert.Contains("/file-dir-a", provider.Current.XmlDirectories);
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