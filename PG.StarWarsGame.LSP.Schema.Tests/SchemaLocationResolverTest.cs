// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Providers;
using PG.StarWarsGame.LSP.Schema.Versioning;

namespace PG.StarWarsGame.LSP.Schema.Tests;

public sealed class SchemaLocationResolverTest
{
    private const string Repository = "AlamoEngine-Tools/eaw-schema";

    private static SchemaHttpCache EmptyCache()
    {
        return new SchemaHttpCache(new FileHelper(new MockFileSystem()), NullLogger<SchemaHttpCache>.Instance);
    }

    private static SchemaHttpCache CacheWithTag(string tag)
    {
        var cache = EmptyCache();
        cache.Update("{}", [], tag: tag);
        return cache;
    }

    private static HttpResponseMessage ReleasesResponse(params string[] tags)
    {
        var body = JsonSerializer.Serialize(tags.Select(t => new { tag_name = t, draft = false, prerelease = false }));
        return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static (SchemaLocationResolver Resolver, FakeHttpMessageHandler Fake) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond, SchemaHttpCache cache)
    {
        var fake = new FakeHttpMessageHandler(respond);
        var releases = new SchemaReleaseResolver(new HttpClient(fake), NullLogger<SchemaReleaseResolver>.Instance);
        return (new SchemaLocationResolver(releases, cache, NullLogger<SchemaLocationResolver>.Instance), fake);
    }

    [Fact]
    public async Task AnExplicitUrl_WinsWithoutAskingForReleases()
    {
        var (resolver, fake) = Build(_ => ReleasesResponse("v2.1.0"), EmptyCache());

        var location = await resolver.ResolveAsync(Repository, "https://example.test/schema/eaw/");

        Assert.Equal(new SchemaLocation("https://example.test/schema/eaw/", null, SchemaOrigin.Explicit), location);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task AResolvedRelease_IsReadFromItsTag()
    {
        var (resolver, _) = Build(_ => ReleasesResponse("v2.1.0", "v2.2.0"), EmptyCache());

        var location = await resolver.ResolveAsync(Repository, null);

        Assert.Equal(new SchemaLocation(
            "https://raw.githubusercontent.com/AlamoEngine-Tools/eaw-schema/refs/tags/v2.2.0/eaw/",
            "v2.2.0", SchemaOrigin.Release), location);
    }

    [Fact]
    public async Task NoReleaseReachable_FallsBackToTheCachedTag()
    {
        var (resolver, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Forbidden), CacheWithTag("v2.1.0"));

        var location = await resolver.ResolveAsync(Repository, null);

        Assert.Equal(new SchemaLocation(
            "https://raw.githubusercontent.com/AlamoEngine-Tools/eaw-schema/refs/tags/v2.1.0/eaw/",
            "v2.1.0", SchemaOrigin.CachedRelease), location);
    }

    // Before the first release exists, or offline on a first start, the server still gets the
    // schema it got before releases existed.
    [Fact]
    public async Task NoReleaseAndNoCache_FallsBackToTheDefaultBranch()
    {
        var (resolver, _) = Build(_ => ReleasesResponse(), EmptyCache());

        var location = await resolver.ResolveAsync(Repository, null);

        Assert.Equal(new SchemaLocation(
            "https://raw.githubusercontent.com/AlamoEngine-Tools/eaw-schema/refs/heads/main/eaw/",
            null, SchemaOrigin.Branch), location);
    }

    [Fact]
    public async Task ABlankExplicitUrl_CountsAsNone()
    {
        var (resolver, _) = Build(_ => ReleasesResponse("v2.1.0"), EmptyCache());

        var location = await resolver.ResolveAsync(Repository, "  ");

        Assert.Equal(SchemaOrigin.Release, location.Origin);
    }
}
