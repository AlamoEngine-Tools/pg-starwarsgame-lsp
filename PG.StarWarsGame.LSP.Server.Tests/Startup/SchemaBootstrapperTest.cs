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
using PG.StarWarsGame.LSP.Schema.Versioning;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Server.Status;

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

    // ── what the bug report is told ──────────────────────────────────────────

    // The provider that ran is dropped once the proxy is configured, and its version check with it,
    // so without this a report could not say which schema the server was actually running.
    [Fact]
    public async Task LoadAsync_RecordsTheSourceAndTheVersionCheck()
    {
        var recorder = new ServerStatusRecorder();
        var bootstrapper = Build(
            new FakeHttpClientFactory(new ManifestHttpHandler("""{ "schemaVersion": "99.0.0" }""")),
            new RecordingUserNotifier(), out _, recorder: recorder);

        await bootstrapper.LoadAsync(CancellationToken.None);

        Assert.Equal(StatusSchemaSource.CustomUrl, recorder.SchemaSource);
        Assert.Equal(SchemaVersionCompatibility.Unsupported, recorder.SchemaCheck?.Compatibility);
        Assert.Equal("99.0.0", recorder.SchemaCheck?.DeclaredVersion);
    }

    // ── schema releases ──────────────────────────────────────────────────────

    private const string ApiHost = "api.github.com";

    private static RoutingHttpHandler ReleaseServer(string releasesJson, string manifestJson = """{ "tags": [] }""")
    {
        return new RoutingHttpHandler(request => request.RequestUri!.Host == ApiHost
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(releasesJson) }
            : request.RequestUri.AbsolutePath.EndsWith("_index.json")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task LoadAsync_DefaultSource_ReadsTheNewestReleaseFromItsTag()
    {
        var recorder = new ServerStatusRecorder();
        var handler = ReleaseServer("""[{ "tag_name": "v2.1.0" }, { "tag_name": "v2.2.0" }]""");
        var bootstrapper = Build(new FakeHttpClientFactory(handler), new RecordingUserNotifier(), out _,
            "", recorder);

        await bootstrapper.LoadAsync(CancellationToken.None);

        Assert.Contains(handler.Urls, u => u.EndsWith("/refs/tags/v2.2.0/eaw/_index.json"));
        Assert.Equal(StatusSchemaSource.Release, recorder.SchemaSource);
        // First load with an empty cache: rebuilt from the network.
        Assert.False(recorder.SchemaFromCache);
    }

    // The Lua API ships in the same repository, so it must come from the same release.
    [Fact]
    public async Task LoadAsync_DefaultSource_ReadsTheLuaApiFromTheSameTag()
    {
        var handler = ReleaseServer("""[{ "tag_name": "v2.2.0" }]""");
        var bootstrapper = Build(new FakeHttpClientFactory(handler), new RecordingUserNotifier(), out _, "");

        await bootstrapper.LoadAsync(CancellationToken.None);

        Assert.Contains(handler.Urls, u => u.EndsWith("/refs/tags/v2.2.0/lua/api.d.lua"));
    }

    [Fact]
    public async Task LoadAsync_DefaultSource_NoRelease_ReadsTheDefaultBranch()
    {
        var recorder = new ServerStatusRecorder();
        var handler = ReleaseServer("[]");
        var bootstrapper = Build(new FakeHttpClientFactory(handler), new RecordingUserNotifier(), out _,
            "", recorder);

        await bootstrapper.LoadAsync(CancellationToken.None);

        Assert.Contains(handler.Urls, u => u.EndsWith("/refs/heads/main/eaw/_index.json"));
        Assert.Equal(StatusSchemaSource.Branch, recorder.SchemaSource);
    }

    [Fact]
    public async Task LoadAsync_ExplicitUrl_NeverAsksForReleases()
    {
        var handler = ReleaseServer("""[{ "tag_name": "v2.2.0" }]""");
        var bootstrapper = Build(new FakeHttpClientFactory(handler), new RecordingUserNotifier(), out _);

        await bootstrapper.LoadAsync(CancellationToken.None);

        Assert.DoesNotContain(handler.Urls, u => u.Contains(ApiHost));
    }

    // ── the Lua stub files ───────────────────────────────────────────────────
    // The schema repository's lua/ directory holds several stub files, listed by lua/_files.json.
    // Every listed file is read, from the same release as the XML schema; a release from before
    // the manifest existed still has api.d.lua alone.

    private static RoutingHttpHandler StubServer(IReadOnlyDictionary<string, string> luaFiles, bool manifest = true)
    {
        return new RoutingHttpHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/lua/_files.json"))
                return manifest
                    ? Ok($$"""{ "files": [{{string.Join(", ", luaFiles.Keys.Select(k => $"\"{k}\""))}}] }""")
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            foreach (var (name, content) in luaFiles)
                if (path.EndsWith("/lua/" + name))
                    return Ok(content);
            return path.EndsWith("_index.json")
                ? Ok("""{ "tags": [] }""")
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        static HttpResponseMessage Ok(string body)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    [Fact]
    public async Task LoadAsync_Http_ReadsEveryStubFileTheManifestLists()
    {
        var handler = StubServer(new Dictionary<string, string>
        {
            ["api.d.lua"] = "function Find_Player(name) end\n",
            ["stdlib.d.lua"] = "function tostring(v) end\n"
        });
        var bootstrapper = Build(new FakeHttpClientFactory(handler), new RecordingUserNotifier(), out _,
            luaProxy: out var lua);

        await bootstrapper.LoadAsync(CancellationToken.None);

        Assert.Contains(handler.Urls, u => u.EndsWith("/lua/_files.json"));
        Assert.Contains("Find_Player", lua.AllFunctionNames);
        Assert.Contains("tostring", lua.AllFunctionNames);
    }

    [Fact]
    public async Task LoadAsync_Http_NoManifest_ReadsTheApiFileAlone()
    {
        var handler = StubServer(new Dictionary<string, string>
        {
            ["api.d.lua"] = "function Find_Player(name) end\n"
        }, manifest: false);
        var bootstrapper = Build(new FakeHttpClientFactory(handler), new RecordingUserNotifier(), out _,
            luaProxy: out var lua);

        await bootstrapper.LoadAsync(CancellationToken.None);

        Assert.Contains(handler.Urls, u => u.EndsWith("/lua/api.d.lua"));
        Assert.Contains("Find_Player", lua.AllFunctionNames);
    }

    // The disk loader is exercised directly: the local XML provider around it needs a file-system
    // watcher the mock file system does not have, and the stubs do not depend on it.
    [Fact]
    public void LoadLuaSchemaFromDisk_ReadsEveryStubFileTheManifestLists()
    {
        var fs = new MockFileSystem();
        fs.AddFile(@"C:\schema\lua\_files.json", new MockFileData("""{ "files": ["api.d.lua", "stdlib.d.lua"] }"""));
        fs.AddFile(@"C:\schema\lua\api.d.lua", new MockFileData("function Find_Player(name) end\n"));
        fs.AddFile(@"C:\schema\lua\stdlib.d.lua", new MockFileData("function tostring(v) end\n"));
        var bootstrapper = Build(new FakeHttpClientFactory(StubServer(new Dictionary<string, string>())),
            new RecordingUserNotifier(), out _, out var lua, fileSystem: fs);

        bootstrapper.LoadLuaSchemaFromDisk(@"C:\schema\eaw");

        Assert.Contains("Find_Player", lua.AllFunctionNames);
        Assert.Contains("tostring", lua.AllFunctionNames);
    }

    [Fact]
    public void LoadLuaSchemaFromDisk_NoManifest_ReadsTheApiFileAlone()
    {
        var fs = new MockFileSystem();
        fs.AddFile(@"C:\schema\lua\api.d.lua", new MockFileData("function Find_Player(name) end\n"));
        var bootstrapper = Build(new FakeHttpClientFactory(StubServer(new Dictionary<string, string>())),
            new RecordingUserNotifier(), out _, out var lua, fileSystem: fs);

        bootstrapper.LoadLuaSchemaFromDisk(@"C:\schema\eaw");

        Assert.Contains("Find_Player", lua.AllFunctionNames);
    }

    [Fact]
    public void LoadLuaSchemaFromDisk_ListedFileMissing_LoadsTheOthers()
    {
        var fs = new MockFileSystem();
        fs.AddFile(@"C:\schema\lua\_files.json", new MockFileData("""{ "files": ["api.d.lua", "stdlib.d.lua"] }"""));
        fs.AddFile(@"C:\schema\lua\api.d.lua", new MockFileData("function Find_Player(name) end\n"));
        var bootstrapper = Build(new FakeHttpClientFactory(StubServer(new Dictionary<string, string>())),
            new RecordingUserNotifier(), out _, out var lua, fileSystem: fs);

        bootstrapper.LoadLuaSchemaFromDisk(@"C:\schema\eaw");

        Assert.Contains("Find_Player", lua.AllFunctionNames);
    }

    private static SchemaBootstrapper Build(IHttpClientFactory factory)
    {
        return Build(factory, new RecordingUserNotifier(), out _);
    }

    private static SchemaBootstrapper Build(
        IHttpClientFactory factory, IUserNotifier notifier, out SchemaProviderProxy proxy,
        string url = "https://example.com/eaw/", ServerStatusRecorder? recorder = null)
    {
        return Build(factory, notifier, out proxy, out _, url, recorder);
    }

    private static SchemaBootstrapper Build(
        IHttpClientFactory factory, IUserNotifier notifier, out SchemaProviderProxy proxy,
        out LuaApiSchemaProxy luaProxy,
        string url = "https://example.com/eaw/", ServerStatusRecorder? recorder = null,
        MockFileSystem? fileSystem = null, SchemaSourceConfig? source = null)
    {
        var fs = fileSystem ?? new MockFileSystem();
        var fileHelper = new FileHelper(fs);
        var config = new FakeConfigProvider(new LspConfiguration
        {
            SchemaSource = source ?? new SchemaSourceConfig
            {
                Type = SchemaSourceType.Http,
                Url = url
            }
        });

        var cache = new SchemaHttpCache(fileHelper, new NullCrossProcessLock(), NullLogger<SchemaHttpCache>.Instance);
        var locations = new SchemaLocationResolver(
            new SchemaReleaseResolver(factory.CreateClient(nameof(SchemaReleaseResolver)),
                NullLogger<SchemaReleaseResolver>.Instance),
            cache, NullLogger<SchemaLocationResolver>.Instance);

        proxy = new SchemaProviderProxy();
        luaProxy = new LuaApiSchemaProxy();
        return new SchemaBootstrapper(
            config,
            proxy,
            luaProxy,
            fs,
            fileHelper,
            factory,
            cache,
            locations,
            notifier,
            NullLogger<SchemaBootstrapper>.Instance,
            NullLogger<LocalFileSchemaProvider>.Instance,
            NullLogger<HttpSchemaProvider>.Instance,
            recorder);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Answers through a delegate and records every URL asked for.</summary>
    private sealed class RoutingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Urls)
                Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

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