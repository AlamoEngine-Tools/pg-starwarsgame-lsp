// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Providers;
using PG.StarWarsGame.LSP.Schema.Versioning;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     The version gate as the providers apply it: a schema whose MAJOR this server does not
///     implement must leave the index empty rather than half-loaded.
/// </summary>
public sealed class SchemaVersionWiringTest : IDisposable
{
    private const string TagsYaml = """
                                    tags:
                                      - tag: Name
                                        type: NameReference
                                    """;

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public SchemaVersionWiringTest()
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
            // Best-effort cleanup.
        }
    }

    // ── local provider ───────────────────────────────────────────────────────

    private LocalFileSchemaProvider CreateLocal(string? indexJson)
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "tags"));
        File.WriteAllText(Path.Combine(_tempDir, "tags", "GameObjectType.yaml"), TagsYaml);
        if (indexJson is not null)
            File.WriteAllText(Path.Combine(_tempDir, "_index.json"), indexJson);
        return new LocalFileSchemaProvider(_tempDir, new FileSystem(), NullLogger<LocalFileSchemaProvider>.Instance);
    }

    [Fact]
    public void Local_SupportedVersion_Loads()
    {
        using var provider = CreateLocal("""{ "schemaVersion": "1.0.0" }""");

        Assert.Single(provider.AllTags);
        Assert.Equal(SchemaVersionCompatibility.Supported, provider.LastVersionCheck?.Compatibility);
    }

    [Fact]
    public void Local_NoIndexFile_LoadsAsUnversioned()
    {
        using var provider = CreateLocal(null);

        Assert.Single(provider.AllTags);
        Assert.Equal(SchemaVersionCompatibility.Unversioned, provider.LastVersionCheck?.Compatibility);
    }

    [Fact]
    public void Local_NewerMajor_LoadsNothing()
    {
        using var provider = CreateLocal("""{ "schemaVersion": "2.0.0" }""");

        Assert.Empty(provider.AllTags);
        Assert.Equal(SchemaVersionCompatibility.Unsupported, provider.LastVersionCheck?.Compatibility);
    }

    [Fact]
    public void Local_MalformedVersion_LoadsNothing()
    {
        using var provider = CreateLocal("""{ "schemaVersion": "not-a-version" }""");

        Assert.Empty(provider.AllTags);
        Assert.Equal(SchemaVersionCompatibility.Malformed, provider.LastVersionCheck?.Compatibility);
    }

    // A manifest we cannot even parse must not be read as "no version declared" - that would turn a
    // corrupt schema into a silently-accepted one.
    [Fact]
    public void Local_UnparseableIndexFile_LoadsNothing()
    {
        using var provider = CreateLocal("{ this is not json");

        Assert.Empty(provider.AllTags);
        Assert.Equal(SchemaVersionCompatibility.Malformed, provider.LastVersionCheck?.Compatibility);
    }

    // ── http provider ────────────────────────────────────────────────────────

    private static SchemaHttpCache NoOpCache()
    {
        return new SchemaHttpCache(new FileHelper(new MockFileSystem()), NullLogger<SchemaHttpCache>.Instance);
    }

    private static HttpSchemaProvider CreateHttp(string indexJson)
    {
        var handler = new StubHandler(new Dictionary<string, string>
        {
            ["_index.json"] = indexJson,
            ["tags/GameObjectType.yaml"] = TagsYaml
        });
        return new HttpSchemaProvider(new HttpClient(handler), "https://schema.test/eaw/",
            NoOpCache(), NullLogger<HttpSchemaProvider>.Instance);
    }

    [Fact]
    public async Task Http_SupportedVersion_Loads()
    {
        var provider = CreateHttp(
            """{ "schemaVersion": "1.0.0", "tags": ["tags/GameObjectType.yaml"] }""");

        await provider.LoadAsync(CancellationToken.None);

        Assert.Single(provider.AllTags);
        Assert.Equal(SchemaVersionCompatibility.Supported, provider.LastVersionCheck?.Compatibility);
    }

    [Fact]
    public async Task Http_NewerMajor_LoadsNothing_AndDoesNotFetchAnyYaml()
    {
        var handler = new StubHandler(new Dictionary<string, string>
        {
            ["_index.json"] = """{ "schemaVersion": "2.0.0", "tags": ["tags/GameObjectType.yaml"] }""",
            ["tags/GameObjectType.yaml"] = TagsYaml
        });
        var provider = new HttpSchemaProvider(new HttpClient(handler), "https://schema.test/eaw/",
            NoOpCache(), NullLogger<HttpSchemaProvider>.Instance);

        await provider.LoadAsync(CancellationToken.None);

        Assert.Empty(provider.AllTags);
        Assert.Equal(SchemaVersionCompatibility.Unsupported, provider.LastVersionCheck?.Compatibility);
        // The gate runs on the manifest alone, so an unusable schema costs one request, not 180.
        Assert.Equal(["_index.json"], handler.RequestedPaths);
    }

    // Awaiters must be released even when the gate refuses, or startup blocks forever.
    [Fact]
    public async Task Http_NewerMajor_StillSignalsReady()
    {
        var provider = CreateHttp(
            """{ "schemaVersion": "2.0.0", "tags": ["tags/GameObjectType.yaml"] }""");

        await provider.LoadAsync(CancellationToken.None);

        await provider.ReadyAsync.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class StubHandler(Dictionary<string, string> byPath) : HttpMessageHandler
    {
        private const string BaseUrl = "https://schema.test/eaw/";
        public List<string> RequestedPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.ToString()[BaseUrl.Length..];
            RequestedPaths.Add(path);
            return Task.FromResult(byPath.TryGetValue(path, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
