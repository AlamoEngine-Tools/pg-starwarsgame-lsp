// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Net;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class BaselineLoaderTest
{
    private static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".aetswg", "baselines");

    private static BaselineIndex MakeBaseline()
    {
        return new BaselineIndex(
            ImmutableDictionary<string, GameSymbol>.Empty
                .Add("UNIT_A", new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "GameObjectType",
                    new FileOrigin("f.xml", 0, null), null)),
            DateTimeOffset.UtcNow,
            "testhash",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("data/xml/units.xml", ImmutableArray.Create("GameObjectType")));
    }

    private static byte[] Serialize(BaselineIndex b)
    {
        return BaselineSerializer.Serialize(b);
    }

    private static byte[] SerializeRawDto(SerializedBaseline dto)
    {
        var msgpack = MessagePackSerializer.Serialize(dto);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal))
            gz.Write(msgpack);
        return ms.ToArray();
    }

    // ── None ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_None_ReturnsEmpty()
    {
        var loader = Build(new MockFileSystem(), new FakeHttpHandler(_ =>
            throw new InvalidOperationException("should not be called")));
        var config = new BaselineSourceConfig { Type = BaselineSourceType.None };

        var result = await loader.LoadAsync(config, CancellationToken.None);

        Assert.Same(BaselineIndex.Empty, result);
    }

    // ── Local ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_Local_FileExists_ReturnsDeserializedBaseline()
    {
        var baseline = MakeBaseline();
        var bytes = Serialize(baseline);
        var path = "/baselines/test.bin";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [path] = new(bytes) });
        var loader = Build(fs, new FakeHttpHandler(_ =>
            throw new InvalidOperationException("should not be called")));
        var config = new BaselineSourceConfig { Type = BaselineSourceType.Local, LocalPath = path };

        var result = await loader.LoadAsync(config, CancellationToken.None);

        Assert.True(result.Symbols.ContainsKey("UNIT_A"));
        Assert.True(result.FileTypeMap.ContainsKey("data/xml/units.xml"));
    }

    [Fact]
    public async Task LoadAsync_Local_FileMissing_ReturnsEmpty()
    {
        var loader = Build(new MockFileSystem(), new FakeHttpHandler(_ =>
            throw new InvalidOperationException("should not be called")));
        var config = new BaselineSourceConfig
        {
            Type = BaselineSourceType.Local,
            LocalPath = "/nonexistent/path.bin"
        };

        var result = await loader.LoadAsync(config, CancellationToken.None);

        Assert.Same(BaselineIndex.Empty, result);
    }

    [Fact]
    public async Task LoadAsync_Local_NullPath_ReturnsEmpty()
    {
        var loader = Build(new MockFileSystem(), new FakeHttpHandler(_ =>
            throw new InvalidOperationException("should not be called")));
        var config = new BaselineSourceConfig { Type = BaselineSourceType.Local, LocalPath = null };

        var result = await loader.LoadAsync(config, CancellationToken.None);

        Assert.Same(BaselineIndex.Empty, result);
    }

    [Fact]
    public async Task LoadAsync_Local_CorruptFile_ReturnsEmpty()
    {
        var path = "/baselines/corrupt.bin";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [path] = new(new byte[] { 0x00, 0x01, 0x02 }) });
        var loader = Build(fs, new FakeHttpHandler(_ =>
            throw new InvalidOperationException("should not be called")));
        var config = new BaselineSourceConfig { Type = BaselineSourceType.Local, LocalPath = path };

        var result = await loader.LoadAsync(config, CancellationToken.None);

        Assert.Same(BaselineIndex.Empty, result);
    }

    [Fact]
    public async Task LoadAsync_Local_WrongSchemaVersion_ReturnsEmpty()
    {
        var path = "/baselines/stale.bin";
        var bytes = SerializeRawDto(new SerializedBaseline
            { SchemaVersion = SerializedBaseline.CurrentSchemaVersion + 99 });
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [path] = new(bytes) });
        var loader = Build(fs, new FakeHttpHandler(_ =>
            throw new InvalidOperationException("should not be called")));
        var config = new BaselineSourceConfig { Type = BaselineSourceType.Local, LocalPath = path };

        var result = await loader.LoadAsync(config, CancellationToken.None);

        Assert.Same(BaselineIndex.Empty, result);
    }

    // ── HTTP ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_Http_Success_ReturnsDeserializedBaseline()
    {
        var baseline = MakeBaseline();
        var bytes = Serialize(baseline);
        var fs = new MockFileSystem();
        var handler = new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var loader = Build(fs, handler);
        var config = new BaselineSourceConfig
        {
            Type = BaselineSourceType.Http,
            Url = "https://example.com/foc-baseline.bin"
        };

        var result = await loader.LoadAsync(config, CancellationToken.None);

        Assert.True(result.Symbols.ContainsKey("UNIT_A"));
    }

    [Fact]
    public async Task LoadAsync_Http_Success_CachesResponseToDisk()
    {
        var baseline = MakeBaseline();
        var bytes = Serialize(baseline);
        var fs = new MockFileSystem();
        var handler = new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var loader = Build(fs, handler);
        var config = new BaselineSourceConfig
        {
            Type = BaselineSourceType.Http,
            Url = "https://example.com/foc-baseline.bin"
        };

        await loader.LoadAsync(config, CancellationToken.None);

        var cacheFile = Path.Combine(CacheDir, "foc-baseline.bin");
        Assert.True(fs.File.Exists(cacheFile));
    }

    [Fact]
    public async Task LoadAsync_Http_DownloadFails_CacheExists_ReturnsCachedBaseline()
    {
        var baseline = MakeBaseline();
        var bytes = Serialize(baseline);
        var cacheFile = Path.Combine(CacheDir, "foc-baseline.bin");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [cacheFile] = new(bytes) });
        var handler = new FakeHttpHandler(_ => throw new HttpRequestException("network error"));
        var loader = Build(fs, handler);
        var config = new BaselineSourceConfig
        {
            Type = BaselineSourceType.Http,
            Url = "https://example.com/foc-baseline.bin"
        };

        var result = await loader.LoadAsync(config, CancellationToken.None);

        Assert.True(result.Symbols.ContainsKey("UNIT_A"));
    }

    [Fact]
    public async Task LoadAsync_Http_DownloadFails_NoCacheExists_ReturnsEmpty()
    {
        var fs = new MockFileSystem();
        var handler = new FakeHttpHandler(_ => throw new HttpRequestException("network error"));
        var loader = Build(fs, handler);
        var config = new BaselineSourceConfig
        {
            Type = BaselineSourceType.Http,
            Url = "https://example.com/foc-baseline.bin"
        };

        var result = await loader.LoadAsync(config, CancellationToken.None);

        Assert.Same(BaselineIndex.Empty, result);
    }

    [Fact]
    public async Task LoadAsync_Http_WrongSchemaVersion_NoCacheExists_ReturnsEmpty()
    {
        var bytes = SerializeRawDto(new SerializedBaseline
            { SchemaVersion = SerializedBaseline.CurrentSchemaVersion + 99 });
        var fs = new MockFileSystem();
        var handler = new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var loader = Build(fs, handler);
        var config = new BaselineSourceConfig
        {
            Type = BaselineSourceType.Http,
            Url = "https://example.com/foc-baseline.bin"
        };

        var result = await loader.LoadAsync(config, CancellationToken.None);

        Assert.Same(BaselineIndex.Empty, result);
    }

    [Fact]
    public async Task LoadAsync_Http_WrongSchemaVersion_CacheExists_FallsBackToCache()
    {
        var staleBytes = SerializeRawDto(new SerializedBaseline
            { SchemaVersion = SerializedBaseline.CurrentSchemaVersion + 99 });
        var cachedBaseline = MakeBaseline();
        var cachedBytes = Serialize(cachedBaseline);
        var cacheFile = Path.Combine(CacheDir, "foc-baseline.bin");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [cacheFile] = new(cachedBytes) });
        var handler = new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(staleBytes) });
        var loader = Build(fs, handler);
        var config = new BaselineSourceConfig
        {
            Type = BaselineSourceType.Http,
            Url = "https://example.com/foc-baseline.bin"
        };

        var result = await loader.LoadAsync(config, CancellationToken.None);

        Assert.True(result.Symbols.ContainsKey("UNIT_A"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static BaselineLoader Build(MockFileSystem fs, FakeHttpHandler handler)
    {
        var client = new HttpClient(handler);
        return new BaselineLoader(client, new FileHelper(fs), NullLogger<BaselineLoader>.Instance);
    }

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}