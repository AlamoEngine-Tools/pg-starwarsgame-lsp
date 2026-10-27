// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     Particle files reach the Problems view on the workspace sweep only, with the preview's
///     own ids, and only the ones with something to say.
/// </summary>
public sealed class ParticleFileDiagnosticsPublisherTest
{
    private const string CleanPath = "data/art/models/p_clean.alo";
    private const string FlaggedPath = "data/art/models/p_flagged.alo";
    private const string PackedPath = "data/art/models/p_packed.alo";

    [Fact]
    public async Task RepublishAll_PublishesTheFlaggedFileWithTheParticleIds_AndNothingForTheRest()
    {
        var published = new List<PublishDiagnosticsParams>();
        var publisher = Publisher(published, new FakeAssets()
            .With(CleanPath, ParticleSystem(Emitter()))
            .With(FlaggedPath, ParticleSystem(Emitter(extra: Chunk(0x007F, false, I32(1)))))
            .With(PackedPath, ParticleSystem(Emitter(extra: Chunk(0x007F, false, I32(1)))), packed: true));

        await publisher.RepublishAllAsync(CancellationToken.None);

        var p = Assert.Single(published);
        Assert.EndsWith("p_flagged.alo", p.Uri.ToUnencodedString());
        var d = Assert.Single(p.Diagnostics);
        Assert.Equal(DiagnosticIds.PreviewParticleUnreadByGame.ToString(), d.Code?.String);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("0x7F", d.Message);
        Assert.Equal(0, d.Range.Start.Line);
    }

    [Fact]
    public async Task RepublishAll_ReportsAFileTheGameWouldFailToLoadAsAnError()
    {
        var published = new List<PublishDiagnosticsParams>();
        var broken = Chunk(0x0900, true, Concat(
            Chunk(0x0000, false, Str("p_broken")),
            Chunk(0x0001, false, I32(0)),
            Chunk(0x0800, true, Concat(Chunk(0x007F, false, I32(1))))));
        var publisher = Publisher(published, new FakeAssets().With(FlaggedPath, broken));

        await publisher.RepublishAllAsync(CancellationToken.None);

        var d = Assert.Single(Assert.Single(published).Diagnostics);
        Assert.Equal(DiagnosticIds.PreviewModelUnreadable.ToString(), d.Code?.String);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public async Task RepublishAll_ClearsWhatItPublishedLastTime()
    {
        var published = new List<PublishDiagnosticsParams>();
        var assets = new FakeAssets()
            .With(FlaggedPath, ParticleSystem(Emitter(extra: Chunk(0x007F, false, I32(1)))));
        var publisher = Publisher(published, assets);

        await publisher.RepublishAllAsync(CancellationToken.None);
        assets.Replace(FlaggedPath, ParticleSystem(Emitter()));
        await publisher.RepublishAllAsync(CancellationToken.None);

        // The second sweep finds nothing and must take the first sweep's finding back.
        Assert.Equal(2, published.Count);
        Assert.Empty(published[1].Diagnostics);
        Assert.Equal(published[0].Uri, published[1].Uri);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static ParticleFileDiagnosticsPublisher Publisher(List<PublishDiagnosticsParams> published,
        FakeAssets assets)
    {
        var index = GameIndex.Empty with { AssetFiles = assets };
        var fileHelper = new FileHelper(new MockFileSystem());
        return new ParticleFileDiagnosticsPublisher(new RecordingDiagnosticsSink(published),
            new FakeGameIndexService(index), new FakeGameWorkspaceHost(), assets, fileHelper,
            new FakeLspConfigurationProvider(), new FakeGlobalSuppressionStore(), new ProjectLayerMap(fileHelper),
            TestServerOptions.Immediate, NullLogger<ParticleFileDiagnosticsPublisher>.Instance);
    }

    private sealed class FakeAssets : IGameAssetResolver, IAssetFileIndex
    {
        private readonly Dictionary<string, (byte[] Bytes, bool Packed)> _files =
            new(StringComparer.OrdinalIgnoreCase);

        public GameAssetTiers Tiers => new(0, false, false, 0);

        public bool Contains(string normalisedPath)
        {
            return _files.ContainsKey(normalisedPath);
        }

        public IEnumerable<string> GetByExtension(string ext)
        {
            return _files.Keys.Where(k => k.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<string> GetByFileName(string fileName)
        {
            return _files.Keys.Where(k => k.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsPackedAsset(string normalisedPath)
        {
            return _files.TryGetValue(normalisedPath, out var f) && f.Packed;
        }

        public GameAssetLocation? Locate(string gameRelativePath)
        {
            return _files.ContainsKey(gameRelativePath)
                ? new GameAssetLocation(gameRelativePath, @"C:\mod\" + gameRelativePath.Replace('/', '\\'),
                    GameAssetTier.Workspace)
                : null;
        }

        public byte[]? Read(string gameRelativePath)
        {
            return _files.TryGetValue(gameRelativePath, out var f) ? f.Bytes : null;
        }

        public FakeAssets With(string path, byte[] bytes, bool packed = false)
        {
            _files[path] = (bytes, packed);
            return this;
        }

        public void Replace(string path, byte[] bytes)
        {
            _files[path] = (bytes, _files[path].Packed);
        }
    }

    // ── the smallest particle file that parses ────────────────────────────────

    private static byte[] ParticleSystem(params byte[][] emitters)
    {
        return Chunk(0x0900, true, Concat(
            Chunk(0x0000, false, Str("p_test")),
            Chunk(0x0001, false, I32(0)),
            Chunk(0x0800, true, Concat(emitters))));
    }

    private static byte[] Emitter(byte[]? extra = null)
    {
        var volume = Chunk(0x1100, true, Chunk(0x1101, false, new byte[64]));
        var parts = new List<byte[]>
        {
            Chunk(0x0002, true, Concat()),
            Chunk(0x0003, false, Str("p_particle_master.tga")),
            Chunk(0x0016, false, Str("spark")),
            Chunk(0x0029, true, Concat(volume, volume, volume)),
            Chunk(0x0001, true, Tracks())
        };
        if (extra is not null)
            parts.Add(extra);
        return Chunk(0x0700, true, Concat([.. parts]));
    }

    private static byte[] Tracks()
    {
        var parts = new List<byte[]>();
        for (var i = 0; i < 7; i++)
        {
            var byteValued = i < 4;
            var endpoint = byteValued ? new byte[] { 255 } : F32(1f);
            parts.Add(Chunk(0x0000, true, Concat(Mini(0x02, endpoint), Mini(0x03, endpoint), Mini(0x04, I32(0)))));
            parts.Add(Chunk(0x0001, true, Concat()));
        }

        return Concat([.. parts]);
    }

    private static byte[] Chunk(uint type, bool container, params byte[][] body)
    {
        var data = Concat(body);
        var header = new byte[8];
        BitConverter.GetBytes(type).CopyTo(header, 0);
        BitConverter.GetBytes((uint)data.Length | (container ? 0x80000000u : 0u)).CopyTo(header, 4);
        return Concat(header, data);
    }

    private static byte[] Mini(byte type, params byte[][] body)
    {
        var data = Concat(body);
        return Concat([type, (byte)data.Length], data);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        return parts.SelectMany(p => p).ToArray();
    }

    private static byte[] Str(string value)
    {
        return Concat(Encoding.ASCII.GetBytes(value), [0]);
    }

    private static byte[] I32(int value)
    {
        return BitConverter.GetBytes(value);
    }

    private static byte[] F32(float value)
    {
        return BitConverter.GetBytes(value);
    }
}