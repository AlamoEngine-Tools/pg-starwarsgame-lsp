// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Schema.Versioning;

namespace PG.StarWarsGame.LSP.Schema.Tests;

public sealed class SchemaReleaseResolverTest
{
    private const string Repository = "AlamoEngine-Tools/eaw-schema";
    private const string Range = ">=2.0.0 <3.0.0";

    private static object Release(string tag, bool draft = false, bool prerelease = false)
    {
        return new { tag_name = tag, draft, prerelease };
    }

    private static HttpResponseMessage Releases(params object[] releases)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(releases), Encoding.UTF8, "application/json")
        };
    }

    private static (SchemaReleaseResolver Resolver, FakeHttpMessageHandler Fake) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var fake = new FakeHttpMessageHandler(respond);
        var resolver = new SchemaReleaseResolver(new HttpClient(fake), NullLogger<SchemaReleaseResolver>.Instance);
        return (resolver, fake);
    }

    private static async Task<SchemaRelease?> Resolve(params object[] releases)
    {
        var (resolver, _) = Build(_ => Releases(releases));
        return await resolver.ResolveAsync(Repository, Range);
    }

    // ── choosing a release ───────────────────────────────────────────────────

    [Fact]
    public async Task TheHighestReleaseInRange_Wins()
    {
        var release = await Resolve(Release("v2.1.0"), Release("v2.2.0"), Release("v2.0.5"));

        Assert.Equal("v2.2.0", release?.Tag);
        Assert.Equal("2.2.0", release?.Version.ToString());
    }

    [Fact]
    public async Task ANewerMajor_IsSkipped()
    {
        var release = await Resolve(Release("v3.0.0"), Release("v2.1.0"));

        Assert.Equal("v2.1.0", release?.Tag);
    }

    [Fact]
    public async Task AnOlderMajorOnly_ResolvesNothing()
    {
        Assert.Null(await Resolve(Release("v1.9.0")));
    }

    [Fact]
    public async Task ADraft_IsSkipped()
    {
        var release = await Resolve(Release("v2.3.0", draft: true), Release("v2.1.0"));

        Assert.Equal("v2.1.0", release?.Tag);
    }

    [Fact]
    public async Task AReleaseMarkedPrerelease_IsSkipped()
    {
        var release = await Resolve(Release("v2.3.0", prerelease: true), Release("v2.1.0"));

        Assert.Equal("v2.1.0", release?.Tag);
    }

    // A tag can carry a prerelease suffix without the release being flagged as one; the suffix
    // is what semver means by "not ready", so it decides.
    [Fact]
    public async Task APrereleaseSuffix_IsSkippedEvenWhenTheReleaseIsNotFlagged()
    {
        var release = await Resolve(Release("v2.3.0-rc.1"), Release("v2.1.0"));

        Assert.Equal("v2.1.0", release?.Tag);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("v2.1")]
    [InlineData("schema-2.1.0")]
    [InlineData("v2.1.0.4")]
    public async Task AMalformedTag_IsSkipped(string tag)
    {
        var release = await Resolve(Release(tag), Release("v2.0.0"));

        Assert.Equal("v2.0.0", release?.Tag);
    }

    [Fact]
    public async Task ATagWithoutThePrefix_IsAccepted()
    {
        var release = await Resolve(Release("2.1.0"));

        Assert.Equal("2.1.0", release?.Tag);
    }

    [Fact]
    public async Task NoReleases_ResolvesNothing()
    {
        Assert.Null(await Resolve());
    }

    // ── failures resolve nothing, so the caller can fall back to its cache ──

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)] // unauthenticated rate limit
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnErrorStatus_ResolvesNothing(HttpStatusCode status)
    {
        var (resolver, _) = Build(_ => new HttpResponseMessage(status));

        Assert.Null(await resolver.ResolveAsync(Repository, Range));
    }

    [Fact]
    public async Task ANetworkFailure_ResolvesNothing()
    {
        var resolver = new SchemaReleaseResolver(
            new HttpClient(new FakeFailingHttpHandler(new HttpRequestException("offline"))),
            NullLogger<SchemaReleaseResolver>.Instance);

        Assert.Null(await resolver.ResolveAsync(Repository, Range));
    }

    [Fact]
    public async Task AnUnreadableBody_ResolvesNothing()
    {
        var (resolver, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not json</html>")
        });

        Assert.Null(await resolver.ResolveAsync(Repository, Range));
    }

    [Fact]
    public async Task ACallerCancellation_IsNotSwallowed()
    {
        var (resolver, _) = Build(_ => Releases(Release("v2.1.0")));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.ResolveAsync(Repository, Range, cts.Token));
    }

    // ── the request ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TheRequest_ListsTheRepositorysReleases()
    {
        var (resolver, fake) = Build(_ => Releases(Release("v2.1.0")));

        await resolver.ResolveAsync(Repository, Range);

        var request = Assert.Single(fake.Requests);
        Assert.Equal("https://api.github.com/repos/AlamoEngine-Tools/eaw-schema/releases?per_page=100",
            request.RequestUri!.ToString());
    }

    // GitHub rejects API requests without a User-Agent.
    [Fact]
    public async Task TheRequest_CarriesAUserAgentAndTheGitHubMediaType()
    {
        var (resolver, fake) = Build(_ => Releases(Release("v2.1.0")));

        await resolver.ResolveAsync(Repository, Range);

        var request = Assert.Single(fake.Requests);
        Assert.NotEmpty(request.Headers.UserAgent);
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/vnd.github+json");
    }

    // The range defaults to what this server implements, so the gate and the resolver cannot drift.
    [Fact]
    public async Task TheDefaultRange_IsTheGatesRange()
    {
        var (resolver, _) = Build(_ => Releases(Release("v3.0.0"), Release("v2.4.1")));

        var release = await resolver.ResolveAsync(Repository);

        Assert.Equal("v2.4.1", release?.Tag);
    }
}
