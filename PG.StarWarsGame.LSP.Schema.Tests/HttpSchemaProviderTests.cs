using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Providers;

namespace PG.StarWarsGame.LSP.Schema.Tests;

public sealed class HttpSchemaProviderTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private const string BaseUrl = "http://schema.test/";

    private static HttpResponseMessage JsonResponse(object payload, string? etag = null)
    {
        var msg = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload),
                Encoding.UTF8, "application/json")
        };
        if (etag is not null)
            msg.Headers.ETag = new EntityTagHeaderValue($"\"{etag}\"");
        return msg;
    }

    private static HttpResponseMessage YamlResponse(string yaml, string? etag = null)
    {
        var msg = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(yaml)
        };
        if (etag is not null)
            msg.Headers.ETag = new EntityTagHeaderValue($"\"{etag}\"");
        return msg;
    }

    private static HttpResponseMessage NotModified()
    {
        return new HttpResponseMessage(HttpStatusCode.NotModified);
    }

    private static SchemaHttpCache NoOpCache()
    {
        return new SchemaHttpCache(new MockFileSystem(), NullLogger<SchemaHttpCache>.Instance);
    }

    private static (HttpSchemaProvider provider, FakeHttpMessageHandler fake) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var fake = new FakeHttpMessageHandler(respond);
        var client = new HttpClient(fake);
        var provider = new HttpSchemaProvider(client, BaseUrl, NoOpCache(), NullLogger<HttpSchemaProvider>.Instance);
        return (provider, fake);
    }

    // ── LoadAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_FetchesIndexThenYamlFiles()
    {
        var manifest = new { tags = new[] { "tags/Unit.yaml", "tags/Faction.yaml" }, types = Array.Empty<string>() };

        var (provider, fake) = Build(req =>
            req.RequestUri!.AbsolutePath.EndsWith("_index.json")
                ? JsonResponse(manifest)
                : YamlResponse("tags:\n  - tag: Foo\n    type: Float\n"));

        await provider.LoadAsync();

        // 1 manifest + 2 YAML = 3 requests
        Assert.Equal(3, fake.Requests.Count);
    }

    [Fact]
    public async Task LoadAsync_AllTagsIndexed()
    {
        var manifest = new { tags = new[] { "tags/Unit.yaml" }, types = Array.Empty<string>() };
        const string yaml = """
                            tags:
                              - tag: Tactical_Health
                                type: Float
                              - tag: Shield_Points
                                type: Float
                            """;

        var (provider, _) = Build(req =>
            req.RequestUri!.AbsolutePath.EndsWith("_index.json")
                ? JsonResponse(manifest)
                : YamlResponse(yaml));

        await provider.LoadAsync();

        Assert.Equal(2, provider.AllTags.Count);
        Assert.NotNull(provider.GetTag("Tactical_Health"));
    }

    [Fact]
    public async Task LoadAsync_TypeNameExtractedFromPath()
    {
        var manifest = new { tags = new[] { "tags/GameObjectType.yaml" }, types = Array.Empty<string>() };
        const string yaml = "tags:\n  - tag: Mass\n    type: Float\n";

        var (provider, _) = Build(req =>
            req.RequestUri!.AbsolutePath.EndsWith("_index.json")
                ? JsonResponse(manifest)
                : YamlResponse(yaml));

        await provider.LoadAsync();

        Assert.Equal(1, provider.GetTagsForType("GameObjectType").Count);
    }

    [Fact]
    public async Task LoadAsync_SecondLoad_SameManifest_OnlyFetchesIndex()
    {
        // When _index.json content hasn't changed the cache checksum matches,
        // so the second load skips all YAML downloads and serves from disk.
        var manifest = new { tags = new[] { "tags/Unit.yaml" }, types = Array.Empty<string>() };
        const string yaml = "tags:\n  - tag: Foo\n    type: Float\n";

        var (provider, fake) = Build(req =>
            req.RequestUri!.AbsolutePath.EndsWith("_index.json")
                ? JsonResponse(manifest)
                : YamlResponse(yaml, "v1"));

        await provider.LoadAsync(); // first load — downloads + populates cache
        fake.Requests.Clear();

        await provider.LoadAsync(); // second load — cache hit; only manifest is fetched

        Assert.Single(fake.Requests);
        Assert.EndsWith("_index.json", fake.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task LoadAsync_304_IndexRetainsOldTags()
    {
        var manifest = new { tags = new[] { "tags/Unit.yaml" }, types = Array.Empty<string>() };
        const string yaml = "tags:\n  - tag: Tactical_Health\n    type: Float\n";

        var callCount = 0;
        var (provider, _) = Build(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("_index.json"))
                return JsonResponse(manifest);

            return callCount++ == 0
                ? YamlResponse(yaml, "v1")
                : NotModified();
        });

        await provider.LoadAsync(); // first load — populates index
        await provider.LoadAsync(); // second load — 304, should keep existing

        Assert.NotNull(provider.GetTag("Tactical_Health"));
    }

    [Fact]
    public async Task LoadAsync_SchemaRefreshedEventFired()
    {
        var manifest = new { tags = Array.Empty<string>(), types = Array.Empty<string>() };
        var (provider, _) = Build(_ => JsonResponse(manifest));

        var firedCount = 0;
        provider.SchemaRefreshed += (_, _) => firedCount++;

        await provider.LoadAsync();

        Assert.Equal(1, firedCount);
    }

    [Fact]
    public async Task LoadAsync_TrailingSlashNormalized()
    {
        var manifest = new { tags = Array.Empty<string>(), types = Array.Empty<string>() };

        var fake = new FakeHttpMessageHandler(_ => JsonResponse(manifest));
        var client = new HttpClient(fake);
        // base URL WITHOUT trailing slash
        var provider = new HttpSchemaProvider(client, "http://schema.test", NoOpCache(),
            NullLogger<HttpSchemaProvider>.Instance);

        await provider.LoadAsync();

        // The manifest request URL must not have a double-slash
        var indexRequest = fake.Requests[0];
        Assert.DoesNotContain("//", indexRequest.RequestUri!.AbsolutePath.TrimStart('/'));
        Assert.EndsWith("_index.json", indexRequest.RequestUri.ToString());
    }
    // ── fake HTTP handler ───────────────────────────────────────────────────

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }
}