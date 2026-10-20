// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Schema.Versioning;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>
///     A merge to the baseline repository's <c>main</c> used to publish to every installed client,
///     because the server downloaded from <c>refs/heads/main</c>. The schema stopped doing that in
///     0.4.1: released servers read the newest RELEASE inside the range they support. The baseline
///     gets the same treatment, with the range being the DTO version the server reads, so a server
///     never downloads a baseline it would then refuse.
/// </summary>
public sealed class BaselineLocationResolverTest
{
    private static readonly int Dto = SerializedBaseline.CurrentSchemaVersion;

    private static readonly BaselineSourceConfig Official = new();

    private static HttpResponseMessage Releases(params string[] tags)
    {
        var body = JsonSerializer.Serialize(tags.Select(t => new { tag_name = t, draft = false, prerelease = false }));
        return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static (BaselineLocationResolver Resolver, List<Uri> Requests) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var requests = new List<Uri>();
        var handler = new FakeHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return respond(request);
        });
        var releases = new SchemaReleaseResolver(new HttpClient(handler), NullLogger<SchemaReleaseResolver>.Instance);
        return (new BaselineLocationResolver(releases, NullLogger<BaselineLocationResolver>.Instance), requests);
    }

    [Fact]
    public async Task AnExplicitUrl_WinsWithoutAskingForReleases()
    {
        var (resolver, requests) = Build(_ => Releases($"v{Dto}.1.0"));

        var location = await resolver.ResolveAsync(
            new BaselineSourceConfig { Url = "https://example.test/foc-baseline.aet" });

        Assert.Equal(
            new BaselineLocation("https://example.test/foc-baseline.aet", null, BaselineOrigin.Explicit),
            location);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task TheNewestReleaseInsideTheDtoVersion_IsReadFromItsTag()
    {
        // A tag of the next DTO version is a baseline this server cannot read; it is never chosen
        // however new it is.
        var (resolver, _) = Build(_ => Releases($"v{Dto}.1.0", $"v{Dto}.2.0", $"v{Dto + 1}.0.0"));

        var location = await resolver.ResolveAsync(Official);

        Assert.Equal(new BaselineLocation(
            $"https://raw.githubusercontent.com/AlamoEngine-Tools/eaw-baseline/refs/tags/v{Dto}.2.0/" +
            "foc/aet-pg-swg-lsp-foc-baseline.aet",
            $"v{Dto}.2.0", BaselineOrigin.Release), location);
    }

    [Fact]
    public async Task NoReleaseReachable_FallsBackToTheDefaultBranch()
    {
        // Keeps a server working before the first release exists. The bytes cached by an earlier
        // start are the loader's own fallback, below this one.
        var (resolver, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var location = await resolver.ResolveAsync(Official);

        Assert.Equal(new BaselineLocation(
            "https://raw.githubusercontent.com/AlamoEngine-Tools/eaw-baseline/refs/heads/main/" +
            "foc/aet-pg-swg-lsp-foc-baseline.aet",
            null, BaselineOrigin.Branch), location);
    }

    [Fact]
    public void TheSupportedRange_IsTheDtoVersionsMajor()
    {
        Assert.Equal($">={Dto}.0.0 <{Dto + 1}.0.0", BaselineLocationResolver.SupportedRange);
    }

    [Fact]
    public async Task TheSameSource_IsResolvedOnce()
    {
        // The baseline and its icon sidecar are loaded by two loaders at startup; one release
        // lookup serves both, and both read the same tag.
        var (resolver, requests) = Build(_ => Releases($"v{Dto}.1.0"));

        var first = await resolver.ResolveAsync(Official);
        var second = await resolver.ResolveAsync(new BaselineSourceConfig());

        Assert.Equal(first, second);
        Assert.Single(requests);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(respond(request));
        }
    }
}