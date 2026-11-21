// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;
using static PG.StarWarsGame.LSP.Server.Tests.Preview.ParticleFileBytes;

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
        Assert.Contains("0x7F", d.Message.Text());
        Assert.Equal(0, d.Range.Start.Line);
    }

    [Fact]
    public async Task RepublishAll_SaysEachSkippedChunkOncePerFile()
    {
        // #394: three emitters carrying the same foreign chunk and the Particle Editor's link group,
        // and one more chunk at the system's own level. One line each, naming the emitters - not a
        // "skipped" line per emitter that reads as if the system were.
        var linked = Concat(Chunk(0x007F, false, I32(1)), Chunk(0x0100, false, I32(3)));
        var published = new List<PublishDiagnosticsParams>();
        var publisher = Publisher(published, new FakeAssets().With(FlaggedPath,
            ParticleSystemWith(Chunk(0x007E, false, I32(1)), Emitter(linked), Emitter(linked), Emitter(linked))));

        await publisher.RepublishAllAsync(CancellationToken.None);

        Assert.Equal([
                "Emitters 0-2 - chunk 0x7F is not read by the game and has no effect",
                "Emitters 0-2 - the Particle Editor's link group is not read by the game and has no effect",
                "Particle system - chunk 0x7E is not read by the game and has no effect"
            ],
            Assert.Single(published).Diagnostics.Select(d => d.Message.Text()));
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
            TestCatalogues.English, new FakeGlobalSuppressionStore(), new ProjectLayerMap(fileHelper),
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
}