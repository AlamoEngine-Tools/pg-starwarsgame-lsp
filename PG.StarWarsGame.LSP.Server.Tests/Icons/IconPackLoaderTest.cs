// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Icons;
using PG.StarWarsGame.LSP.Server.Status;

namespace PG.StarWarsGame.LSP.Server.Tests.Icons;

/// <summary>
///     Which path the icon pack loader took, for the bug report. Each one used to be a log line
///     at most - a missing sidecar is even logged at Debug, by design.
/// </summary>
public sealed class IconPackLoaderTest
{
    private const string BaselineUrl = "https://example.com/foc-baseline.bin";

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aetswg", "baselines");

    private static readonly BaselineSourceConfig HttpSource = new()
        { Type = BaselineSourceType.Http, Url = BaselineUrl };

    private static byte[] Pack()
    {
        return IconPackSerializer.Serialize(
            ImmutableDictionary<string, byte[]>.Empty.Add("i_button_test", [1, 2, 3]), "ab12",
            DateTimeOffset.UnixEpoch);
    }

    private static IconPackLoader Build(MockFileSystem fs, Func<HttpRequestMessage, HttpResponseMessage> respond,
        ServerStatusRecorder recorder)
    {
        return new IconPackLoader(new HttpClient(new FakeHttpHandler(respond)), new FileHelper(fs),
            NullLogger<IconPackLoader>.Instance, recorder);
    }

    [Fact]
    public async Task Http_Success_RecordsNetwork()
    {
        var recorder = new ServerStatusRecorder();
        var bytes = Pack();

        await Build(new MockFileSystem(),
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }, recorder)
            .LoadAsync(HttpSource, CancellationToken.None);

        Assert.Equal(StatusAssetSource.Network, recorder.IconPackSource);
    }

    [Fact]
    public async Task Http_NoSidecar_CacheExists_RecordsCache()
    {
        var recorder = new ServerStatusRecorder();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [Path.Combine(CacheDir, "foc-baseline.bin.icons")] = new(Pack()) });

        await Build(fs, _ => new HttpResponseMessage(HttpStatusCode.NotFound), recorder)
            .LoadAsync(HttpSource, CancellationToken.None);

        Assert.Equal(StatusAssetSource.Cache, recorder.IconPackSource);
    }

    [Fact]
    public async Task Http_NothingUsable_RecordsEmpty()
    {
        var recorder = new ServerStatusRecorder();

        await Build(new MockFileSystem(), _ => new HttpResponseMessage(HttpStatusCode.NotFound), recorder)
            .LoadAsync(HttpSource, CancellationToken.None);

        Assert.Equal(StatusAssetSource.Empty, recorder.IconPackSource);
    }

    [Fact]
    public async Task Local_Loaded_RecordsLocal()
    {
        var recorder = new ServerStatusRecorder();
        var fs = new MockFileSystem();
        fs.AddFile(@"C:\b.bin.icons", new MockFileData(Pack()));

        await Build(fs, _ => new HttpResponseMessage(HttpStatusCode.NotFound), recorder)
            .LoadAsync(new BaselineSourceConfig { Type = BaselineSourceType.Local, LocalPath = @"C:\b.bin" },
                CancellationToken.None);

        Assert.Equal(StatusAssetSource.Local, recorder.IconPackSource);
    }

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(respond(request));
        }
    }
}
