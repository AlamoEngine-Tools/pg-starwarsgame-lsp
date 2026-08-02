// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Schema;
using PG.StarWarsGame.LSP.Schema;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Providers;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Tests.Startup;

public sealed class SchemaBootstrapperTest
{
    [Fact]
    public async Task LoadAsync_HttpSource_StartsEaWAndLuaDownloadsInParallel()
    {
        // Barrier: blocks every HTTP request until at least 2 have started.
        // EaW schema = 1+ requests (first is _index.json); Lua schema = 1 request.
        // If sequential, the first request blocks here waiting for a second that never
        // starts - deadlock. If parallel, both fire, the barrier opens, all requests
        // get (failure) responses and both branches degrade gracefully.
        var handler = new BarrierHttpHandler(2);
        var bootstrapper = Build(new FakeHttpClientFactory(handler));

        var bootTask = bootstrapper.LoadAsync(CancellationToken.None);
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        var first = await Task.WhenAny(bootTask, timeout);

        Assert.True(first == bootTask,
            "EaW schema and Lua schema downloads appear to run sequentially (barrier deadlocked)");
    }

    // ── schema version gate ──────────────────────────────────────────────────

    // A schema the server cannot support is the one failure the user must be told about: nothing
    // works and the log is not where they will look.
    [Fact]
    public async Task LoadAsync_SchemaMajorNotSupported_TellsTheUserAndLoadsNothing()
    {
        var notifier = new RecordingUserNotifier();
        var bootstrapper = Build(
            new FakeHttpClientFactory(new ManifestHttpHandler("""{ "schemaVersion": "99.0.0" }""")),
            notifier, out var proxy);

        await bootstrapper.LoadAsync(CancellationToken.None);

        Assert.Empty(proxy.AllTags);
        var message = Assert.Single(notifier.Errors);
        Assert.Contains("99.0.0", message);
        Assert.Contains("update", message, StringComparison.OrdinalIgnoreCase);
    }

    // Every schema published before schemaVersion existed omits it; warning about that would fire
    // for every current user, so it must stay silent.
    [Fact]
    public async Task LoadAsync_UnversionedSchema_SaysNothingToTheUser()
    {
        var notifier = new RecordingUserNotifier();
        var bootstrapper = Build(
            new FakeHttpClientFactory(new ManifestHttpHandler("""{ "tags": [] }""")),
            notifier, out _);

        await bootstrapper.LoadAsync(CancellationToken.None);

        Assert.Empty(notifier.Errors);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Serves the given manifest for _index.json and 404s everything else.</summary>
    private sealed class ManifestHttpHandler(string manifestJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("_index.json")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static SchemaBootstrapper Build(IHttpClientFactory factory)
    {
        return Build(factory, new RecordingUserNotifier(), out _);
    }

    private static SchemaBootstrapper Build(
        IHttpClientFactory factory, IUserNotifier notifier, out SchemaProviderProxy proxy)
    {
        var fs = new MockFileSystem();
        var fileHelper = new FileHelper(fs);
        var config = new FakeConfigProvider(new LspConfiguration
        {
            SchemaSource = new SchemaSourceConfig
            {
                Type = SchemaSourceType.Http,
                Url = "https://example.com/eaw/"
            }
        });

        proxy = new SchemaProviderProxy();
        return new SchemaBootstrapper(
            config,
            proxy,
            new LuaApiSchemaProxy(),
            fs,
            fileHelper,
            factory,
            new SchemaHttpCache(fileHelper, NullLogger<SchemaHttpCache>.Instance),
            notifier,
            NullLogger<SchemaBootstrapper>.Instance,
            NullLogger<LocalFileSchemaProvider>.Instance,
            NullLogger<HttpSchemaProvider>.Instance);
    }

    private sealed class FakeConfigProvider(LspConfiguration config) : ILspConfigurationProvider
    {
        public LspConfiguration Current => config;

        public void LoadFrom(object? initializationOptions)
        {
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler);
        }
    }

    /// <summary>
    ///     Blocks each request until <paramref name="releaseAfter" /> requests have started,
    ///     then releases all (with 503 - both bootstrappers degrade gracefully on failure).
    /// </summary>
    private sealed class BarrierHttpHandler(int releaseAfter) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _barrier =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _startCount;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _startCount) >= releaseAfter)
                _barrier.TrySetResult();

            await _barrier.Task.WaitAsync(ct);

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }
}