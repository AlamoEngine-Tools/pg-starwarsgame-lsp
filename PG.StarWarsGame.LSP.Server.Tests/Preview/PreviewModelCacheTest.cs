// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Caching;
using PG.StarWarsGame.LSP.Server.Preview;
using PG.StarWarsGame.LSP.Server.Tests.Assets;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The preview cache (#436): a converted model is kept under the project's
///     <c>.aetswg/preview/</c>, keyed by what it was converted from rather than by what it is called.
/// </summary>
public sealed class PreviewModelCacheTest
{
    private const string PreviewDir = "/mod/.aetswg/preview";
    private const string Model = "Data/Art/Models/Hull.alo";
    private const string Twin = "Data/Art/Models/Hull_copy.alo";
    private const string Clip = "Data/Art/Models/Hull_idle_00.ala";

    private static readonly IReadOnlyList<(string Name, string Path)> NoClips = [];
    private static readonly IReadOnlyList<(string Name, string Path)> OneClip = [("Hull_idle_00.ala", Clip)];

    // ── a part and the build agree on one entry ───────────────────────────────

    /// <summary>A judge by which a clip plays on a model at least as long as the clip's first byte says.</summary>
    private sealed class CountingJudge
    {
        public int Calls { get; private set; }
        public List<string> Judged { get; } = [];

        public IReadOnlyList<PreviewClipVerdict> Judge(byte[] model, IReadOnlyList<PreviewClipSource> clips)
        {
            Calls++;
            Judged.AddRange(clips.Select(c => c.Name));
            return
            [
                .. clips.Select(c => c.Bytes[0] <= model.Length
                    ? new PreviewClipVerdict(true, null)
                    : new PreviewClipVerdict(false,
                        new PreviewClipNote(c.Name, PreviewClipNoteKind.CannotPlay, "too many bones")))
            ];
        }
    }

    /// <summary>
    ///     The defect this closes (#436): a unit asks for a hardpoint's model with the HULL's clips,
    ///     the background build converts that model with its OWN, and the key took in every clip
    ///     offered - so the entry the build made was never the entry a unit asked for. Only a clip
    ///     that is baked into the GLB is part of what the GLB is. A hull's clip that reaches past
    ///     the hardpoint's bones is not baked, so it is not part of the key either.
    /// </summary>
    [Fact]
    public void GetOrConvert_ForAPartOfferedClipsThatCannotPlayOnIt_IsAHitOnTheEntryTheBuildMade()
    {
        const string turret = "Data/Art/Models/Turret.alo";
        var assets = new DiskAssets().With(turret, [1, 2, 3]).With(Clip, [9]);
        var converter = new CountingConverter();
        var judge = new CountingJudge();
        var cache = Build(assets);

        // The build: the turret has no clips of its own.
        var built = cache.GetOrConvert(turret, NoClips, converter.Convert, judge.Judge);
        // The unit: the same model, offered the hull's clip, which reaches past its three bones.
        var asPart = cache.GetOrConvert(turret, OneClip, converter.Convert, judge.Judge);

        Assert.Equal(built!.Key, asPart!.Key);
        Assert.Equal(1, converter.Calls);
        Assert.Single(Entries(assets));
        Assert.Empty(asPart.Animations);
        // And the request still says why the clip is not there, a hit included.
        var note = Assert.Single(asPart.Notes);
        Assert.Equal(("Hull_idle_00.ala", PreviewClipNoteKind.CannotPlay), (note.Clip, note.Kind));
        Assert.Empty(built.Notes);
    }

    [Fact]
    public void GetOrConvert_ConvertsWithTheClipsThatPlayOnly_AndKeysByThem()
    {
        const string other = "Data/Art/Models/Other_idle_00.ala";
        var assets = new DiskAssets().With(Model, [1, 2, 3]).With(Clip, [2]).With(other, [9]);
        var converter = new CountingConverter();
        var judge = new CountingJudge();
        var cache = Build(assets);

        var both = cache.GetOrConvert(Model, [("Hull_idle_00.ala", Clip), ("Other_idle_00.ala", other)],
            converter.Convert, judge.Judge);
        var playing = cache.GetOrConvert(Model, OneClip, converter.Convert, judge.Judge);

        Assert.Equal(playing!.Key, both!.Key);
        Assert.Equal(1, converter.Calls);
        Assert.Equal(converter.GlbFor([1, 2, 3], [2]), both.ReadBytes());
        Assert.Equal(["Hull_idle_00"], both.Animations);
    }

    /// <summary>A verdict answers for the bytes it was given: asked once per model and clip, and again when either changes.</summary>
    [Fact]
    public void GetOrConvert_JudgesAModelAndAClipOnce_AndAgainWhenTheClipChanges()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]).With(Clip, [9]);
        var converter = new CountingConverter();
        var judge = new CountingJudge();
        var cache = Build(assets);

        cache.GetOrConvert(Model, OneClip, converter.Convert, judge.Judge);
        cache.GetOrConvert(Model, OneClip, converter.Convert, judge.Judge);
        Assert.Equal(1, judge.Calls);

        assets.With(Clip, [2]);
        var now = cache.GetOrConvert(Model, OneClip, converter.Convert, judge.Judge);

        Assert.Equal(2, judge.Calls);
        Assert.Equal(["Hull_idle_00"], now!.Animations);
        Assert.Empty(now.Notes);
    }

    [Fact]
    public void GetOrConvert_WithNoClipOffered_AsksTheJudgeNothing()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var judge = new CountingJudge();

        Build(assets).GetOrConvert(Model, NoClips, new CountingConverter().Convert, judge.Judge);

        Assert.Equal(0, judge.Calls);
    }

    // ── where the cache lives ─────────────────────────────────────────────────

    /// <summary>
    ///     Under the ROOT project, the highest-ranked layer: a dependency's models converted for
    ///     this project are this project's cache, and its ignore file is the one that lists it.
    /// </summary>
    [Fact]
    public void Location_IsTheRootProjectsPreviewFolder_AndListsItInThatProjectsIgnoreFile()
    {
        var fs = new MockFileSystem();
        var config = new WorkspaceConfiguration([], [], [], [], null)
        {
            Layers =
            [
                new ProjectLayer(0, "core", [], [], [], [], null, "/mods/core/core.pgproj"),
                new ProjectLayer(1, "leaf", [], [], [], [], null, "/mods/leaf/leaf.pgproj")
            ]
        };
        var location = new ProjectPreviewCacheLocation(new StubProjects(config),
            new ProjectIndexCache(new FileHelper(fs), new NullCrossProcessLock(),
                NullLogger<ProjectIndexCache>.Instance));

        location.EnsureIgnored();

        Assert.Equal("/mods/leaf/.aetswg/preview", location.Directory);
        Assert.Contains("preview/", fs.File.ReadAllText("/mods/leaf/.aetswg/.gitignore").Split('\n'));
        Assert.False(fs.Directory.Exists("/mods/core/.aetswg"));
    }

    [Fact]
    public void Location_WithNoProject_IsNowhere()
    {
        var location = new ProjectPreviewCacheLocation(new StubProjects(null), new NullProjectIndexCache());

        location.EnsureIgnored();

        Assert.Null(location.Directory);
    }

    // ── what it keeps ─────────────────────────────────────────────────────────

    private static PreviewModelCache Build(DiskAssets assets, bool project = true,
        int version = PreviewModelCache.ConverterVersion, FixedLocation? location = null,
        IPreviewAssetFingerprints? fingerprints = null)
    {
        return new PreviewModelCache(
            fingerprints ?? new PreviewAssetFingerprints(assets, new FileHelper(assets.Files)),
            new FileHelper(assets.Files),
            location ?? new FixedLocation(project ? PreviewDir : null), NullLogger<PreviewModelCache>.Instance,
            version);
    }

    /// <summary>
    ///     The hole a stamp alone leaves, and what closes it (#436). A copy over a file keeps the
    ///     source's write time on Windows, so a model can be replaced by other bytes of the same
    ///     length under the same write time - MEASURED on a texture copied at 22:06 that still showed
    ///     13:33. The stamp then answers for the OLD bytes. The file watcher sees the write; once it
    ///     has reported the file, the model is read again and converted again.
    /// </summary>
    [Fact]
    public void GetOrConvert_AfterACopyOfTheSameLengthAndWriteTime_ConvertsAgainOnceTheWatcherReportsTheFile()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var converter = new CountingConverter();
        var fingerprints = new PreviewAssetFingerprints(assets, new FileHelper(assets.Files));
        var cache = Build(assets, fingerprints: fingerprints);
        var before = cache.GetOrConvert(Model, NoClips, converter.Convert);

        assets.WithSameStamp(Model, [3, 2, 1]);

        // Nothing has said the file changed, and its stamp has not moved: the old answer stands.
        var unseen = cache.GetOrConvert(Model, NoClips, converter.Convert);
        Assert.Equal(before!.Key, unseen!.Key);
        Assert.Equal(1, converter.Calls);

        // The watcher's spelling of the path, not the resolver's.
        fingerprints.Forget([@"\mod\Data\Art\Models\Hull.alo"]);
        var seen = cache.GetOrConvert(Model, NoClips, converter.Convert);

        Assert.Equal(2, converter.Calls);
        Assert.NotEqual(before.Key, seen!.Key);
        Assert.Equal(converter.GlbFor([3, 2, 1]), seen.ReadBytes());
    }

    /// <summary>A reported file whose bytes did not change is read again and found to be the same model.</summary>
    [Fact]
    public void GetOrConvert_AfterTheWatcherReportsAnUnchangedFile_ReadsItAgainAndConvertsNothing()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var converter = new CountingConverter();
        var fingerprints = new PreviewAssetFingerprints(assets, new FileHelper(assets.Files));
        var cache = Build(assets, fingerprints: fingerprints);
        var before = cache.GetOrConvert(Model, NoClips, converter.Convert);

        fingerprints.Forget(["/mod/" + Model]);
        var after = cache.GetOrConvert(Model, NoClips, converter.Convert);

        Assert.Equal(2, assets.Reads(Model));
        Assert.Equal(1, converter.Calls);
        Assert.Equal(before!.Key, after!.Key);
    }

    // ── the stamp ─────────────────────────────────────────────────────────────

    [Fact]
    public void StampOf_MovesWhenTheWatcherReportsTheFile_ThoughItsLengthAndWriteTimeStand()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var fingerprints = new PreviewAssetFingerprints(assets, new FileHelper(assets.Files));
        var location = assets.Locate(Model)!;
        var before = fingerprints.StampOf(location);

        assets.WithSameStamp(Model, [3, 2, 1]);
        var unreported = fingerprints.StampOf(location);
        fingerprints.Forget(["/mod/" + Model]);
        var reported = fingerprints.StampOf(location);

        Assert.NotNull(before);
        Assert.Equal(before, unreported);
        Assert.NotEqual(before, reported);
    }

    [Fact]
    public void StampOf_MovesWithTheWriteTimeAndWithTheLength()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var fingerprints = new PreviewAssetFingerprints(assets, new FileHelper(assets.Files));
        var first = fingerprints.StampOf(assets.Locate(Model)!);

        assets.With(Model, [1, 2, 3]);
        var later = fingerprints.StampOf(assets.Locate(Model)!);
        assets.WithSameStamp(Model, [1, 2, 3, 4]);
        var longer = fingerprints.StampOf(assets.Locate(Model)!);

        Assert.NotEqual(first, later);
        Assert.NotEqual(later, longer);
    }

    [Fact]
    public void StampOf_AnArchiveEntry_IsNothing()
    {
        var assets = new DiskAssets().WithArchiveEntry(Model, [1, 2, 3]);
        var fingerprints = new PreviewAssetFingerprints(assets, new FileHelper(assets.Files));

        Assert.Null(fingerprints.StampOf(assets.Locate(Model)!));
    }

    [Fact]
    public void Forget_SaysOncePerCallThatSomethingChanged_AndNothingForAnEmptyCall()
    {
        var assets = new DiskAssets();
        var fingerprints = new PreviewAssetFingerprints(assets, new FileHelper(assets.Files));
        var said = 0;
        fingerprints.Changed += () => said++;

        fingerprints.Forget([]);
        fingerprints.Forget(["/mod/a.alo", "/mod/b.dds"]);

        Assert.Equal(1, said);
    }

    private static string[] Entries(DiskAssets assets)
    {
        return assets.Files.Directory.Exists(PreviewDir)
            ? [.. assets.Files.Directory.GetFiles(PreviewDir).Select(f => Path.GetFileName(f)).Order()]
            : [];
    }

    [Fact]
    public void GetOrConvert_ForAModelThatResolvesNowhere_ReturnsNullAndConvertsNothing()
    {
        var assets = new DiskAssets();
        var converter = new CountingConverter();

        var entry = Build(assets).GetOrConvert(Model, NoClips, converter.Convert);

        Assert.Null(entry);
        Assert.Equal(0, converter.Calls);
    }

    /// <summary>
    ///     A deleted file stops naming its entry (FR-12): the name resolves nowhere, so nothing is
    ///     served for it. The entry itself is content another file may share, and stays until the
    ///     cache is cleared.
    /// </summary>
    [Fact]
    public void GetOrConvert_AfterTheModelWasDeleted_ServesNothingForItsName_AndKeepsTheEntryForItsTwin()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]).With(Twin, [1, 2, 3]);
        var converter = new CountingConverter();
        var fingerprints = new PreviewAssetFingerprints(assets, new FileHelper(assets.Files));
        var cache = Build(assets, fingerprints: fingerprints);
        var before = cache.GetOrConvert(Model, NoClips, converter.Convert);

        assets.Files.File.Delete("/mod/" + Model);
        fingerprints.Forget(["/mod/" + Model]);

        Assert.Null(cache.GetOrConvert(Model, NoClips, converter.Convert));
        Assert.Equal([before!.Key + ".aetpm"], Entries(assets));
        Assert.Equal(before.Key, cache.GetOrConvert(Twin, NoClips, converter.Convert)!.Key);
        Assert.Equal(1, converter.Calls);
    }

    [Fact]
    public void GetOrConvert_OnAMiss_ConvertsAndKeepsOneFileUnderPreview()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]).With(Clip, [9]);
        var converter = new CountingConverter();

        var entry = Build(assets).GetOrConvert(Model, OneClip, converter.Convert);

        Assert.NotNull(entry);
        Assert.Equal(PreviewDir + "/" + entry.Key + ".aetpm", entry.File);
        // One file and nothing beside it: no temp file left, no second file to fall out of step.
        Assert.Equal([entry.Key + ".aetpm"], Entries(assets));
        Assert.Equal(converter.GlbFor([1, 2, 3], [9]), entry.ReadBytes());
        Assert.Equal(entry.ReadBytes().Length, entry.Length);
        Assert.Equal(["Hull_idle_00"], entry.Animations);
    }

    // ── clearing ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     The command empties the folder (FR-9): every entry, and the marker of a finished build
    ///     with them, so the next session converts the project's models again.
    /// </summary>
    [Fact]
    public void Clear_RemovesEveryEntryAndTheBuildMarker_AndSaysHowManyEntries()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]).With(Twin, [4, 5, 6]);
        var converter = new CountingConverter();
        var cache = Build(assets);
        cache.GetOrConvert(Model, NoClips, converter.Convert);
        cache.GetOrConvert(Twin, NoClips, converter.Convert);
        assets.Files.File.WriteAllText(PreviewDir + "/" + PreviewCacheBuilder.MarkerFileName, "converter 1\n");

        var removed = cache.Clear();

        Assert.Equal(2, removed);
        Assert.Empty(Entries(assets));
    }

    [Fact]
    public void Clear_ThenTheSameModel_IsConvertedAgain()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var converter = new CountingConverter();
        var cache = Build(assets);
        cache.GetOrConvert(Model, NoClips, converter.Convert);

        cache.Clear();
        var again = cache.GetOrConvert(Model, NoClips, converter.Convert);

        Assert.Equal(2, converter.Calls);
        Assert.Equal([again!.Key + ".aetpm"], Entries(assets));
    }

    [Fact]
    public void Clear_WithNoProjectOrNoCacheYet_RemovesNothing()
    {
        Assert.Equal(0, Build(new DiskAssets(), false).Clear());
        Assert.Equal(0, Build(new DiskAssets()).Clear());
    }

    /// <summary>
    ///     The payload section is the GLB byte for byte, so a hit streams it out of the file as it is.
    /// </summary>
    [Fact]
    public void GetOrConvert_KeepsTheGlbAsThePayloadSectionOfTheEntry()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var converter = new CountingConverter();

        var entry = Build(assets).GetOrConvert(Model, NoClips, converter.Convert);

        var file = assets.Files.File.ReadAllBytes(entry!.File!);
        using var stream = new MemoryStream(file);
        var payload = PreviewCacheEntryFormat.TryReadLayout(stream, file.Length)!
            .Section(PreviewCacheEntryFormat.PayloadSection)!.Value;

        Assert.Equal(converter.GlbFor([1, 2, 3]), file.AsSpan((int)payload.Offset, (int)payload.Length).ToArray());
        using var opened = entry.OpenPayload();
        Assert.Equal(payload.Length, opened.Length);
    }

    /// <summary>
    ///     What the conversion said about a clip is kept with the entry and comes back on a hit, in
    ///     a later session too - the handler writes it to the log again.
    /// </summary>
    [Fact]
    public void GetOrConvert_OnAHit_StillCarriesWhatTheConversionSaid()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]).With(Clip, [9]);
        var converter = new CountingConverter
        {
            Notes = [new PreviewClipNote("Hull_idle_00.ala", PreviewClipNoteKind.DiffersButPlays, "bone 3 is 'b'")]
        };
        Build(assets).GetOrConvert(Model, OneClip, converter.Convert);

        var hit = Build(assets).GetOrConvert(Model, OneClip, converter.Convert);

        Assert.Equal(1, converter.Calls);
        Assert.Equal(converter.Notes, hit!.Notes);
    }

    [Fact]
    public void GetOrConvert_AskedTwice_ConvertsOnceAndReadsTheModelOnce()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]).With(Clip, [9]);
        var converter = new CountingConverter();
        var cache = Build(assets);

        var first = cache.GetOrConvert(Model, OneClip, converter.Convert);
        var second = cache.GetOrConvert(Model, OneClip, converter.Convert);

        Assert.Equal(1, converter.Calls);
        Assert.Equal(first!.Key, second!.Key);
        Assert.Equal(["Hull_idle_00"], second.Animations);
        // The path, the write time and the length answer for a file already hashed.
        Assert.Equal(1, assets.Reads(Model));
        Assert.Equal(1, assets.Reads(Clip));
    }

    /// <summary>
    ///     MEASURED on a mod: 153 of 514 models are byte-identical copies of another under a
    ///     different file name.
    /// </summary>
    [Fact]
    public void GetOrConvert_ForAByteIdenticalModelUnderAnotherName_SharesTheEntry()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]).With(Twin, [1, 2, 3]);
        var converter = new CountingConverter();
        var cache = Build(assets);

        var first = cache.GetOrConvert(Model, NoClips, converter.Convert);
        var second = cache.GetOrConvert(Twin, NoClips, converter.Convert);

        Assert.Equal(1, converter.Calls);
        Assert.Equal(first!.Key, second!.Key);
        Assert.Single(Entries(assets));
    }

    [Fact]
    public void GetOrConvert_AfterTheModelChangedOnDisk_ConvertsAgain()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var converter = new CountingConverter();
        var cache = Build(assets);
        var before = cache.GetOrConvert(Model, NoClips, converter.Convert);

        assets.With(Model, [4, 5, 6, 7]);
        var after = cache.GetOrConvert(Model, NoClips, converter.Convert);

        Assert.Equal(2, converter.Calls);
        Assert.NotEqual(before!.Key, after!.Key);
        Assert.Equal(converter.GlbFor([4, 5, 6, 7]), after.ReadBytes());
    }

    /// <summary>The same length written at a later time is still another file.</summary>
    [Fact]
    public void GetOrConvert_AfterTheModelChangedButKeptItsLength_ConvertsAgain()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var converter = new CountingConverter();
        var cache = Build(assets);
        cache.GetOrConvert(Model, NoClips, converter.Convert);

        assets.With(Model, [3, 2, 1]);
        var after = cache.GetOrConvert(Model, NoClips, converter.Convert);

        Assert.Equal(2, converter.Calls);
        Assert.Equal(converter.GlbFor([3, 2, 1]), after!.ReadBytes());
    }

    [Fact]
    public void GetOrConvert_AfterAClipChangedOnDisk_ConvertsAgain()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]).With(Clip, [9]);
        var converter = new CountingConverter();
        var cache = Build(assets);
        var before = cache.GetOrConvert(Model, OneClip, converter.Convert);

        assets.With(Clip, [8, 8]);
        var after = cache.GetOrConvert(Model, OneClip, converter.Convert);

        Assert.Equal(2, converter.Calls);
        Assert.NotEqual(before!.Key, after!.Key);
    }

    [Fact]
    public void GetOrConvert_WithAndWithoutAClip_AreTwoEntries()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]).With(Clip, [9]);
        var converter = new CountingConverter();
        var cache = Build(assets);

        var bare = cache.GetOrConvert(Model, NoClips, converter.Convert);
        var withClip = cache.GetOrConvert(Model, OneClip, converter.Convert);

        Assert.Equal(2, converter.Calls);
        Assert.NotEqual(bare!.Key, withClip!.Key);
        Assert.Equal(2, Entries(assets).Length);
    }

    /// <summary>A clip named but not there is left out, as the handler always left it out.</summary>
    [Fact]
    public void GetOrConvert_WithAClipThatResolvesNowhere_ConvertsWithoutIt()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var converter = new CountingConverter();
        var cache = Build(assets);

        var entry = cache.GetOrConvert(Model, OneClip, converter.Convert);
        var bare = cache.GetOrConvert(Model, NoClips, converter.Convert);

        Assert.Empty(entry!.Animations);
        Assert.Equal(entry.Key, bare!.Key);
        Assert.Equal(1, converter.Calls);
    }

    [Fact]
    public void GetOrConvert_InALaterSession_FindsTheEntryOnDisk()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]).With(Clip, [9]);
        var converter = new CountingConverter();
        var first = Build(assets).GetOrConvert(Model, OneClip, converter.Convert);

        var second = Build(assets).GetOrConvert(Model, OneClip, converter.Convert);

        Assert.Equal(1, converter.Calls);
        Assert.Equal(first!.Key, second!.Key);
        Assert.Equal(["Hull_idle_00"], second.Animations);
        Assert.Equal(converter.GlbFor([1, 2, 3], [9]), second.ReadBytes());
    }

    [Fact]
    public void GetOrConvert_UnderAnotherConverterVersion_ConvertsAgain()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var converter = new CountingConverter();
        var old = Build(assets, version: 1).GetOrConvert(Model, NoClips, converter.Convert);

        var current = Build(assets, version: 2).GetOrConvert(Model, NoClips, converter.Convert);

        Assert.Equal(2, converter.Calls);
        Assert.NotEqual(old!.Key, current!.Key);
    }

    /// <summary>An archive entry has no file to ask for a write time, so its bytes answer every time.</summary>
    [Fact]
    public void GetOrConvert_ForAnArchiveEntry_KeysByItsBytes()
    {
        var assets = new DiskAssets().WithArchiveEntry(Model, [1, 2, 3]);
        var converter = new CountingConverter();
        var cache = Build(assets);

        var first = cache.GetOrConvert(Model, NoClips, converter.Convert);
        var second = cache.GetOrConvert(Model, NoClips, converter.Convert);

        Assert.Equal(1, converter.Calls);
        Assert.Equal(first!.Key, second!.Key);

        assets.WithArchiveEntry(Model, [7, 7, 7]);
        var changed = cache.GetOrConvert(Model, NoClips, converter.Convert);

        Assert.Equal(2, converter.Calls);
        Assert.NotEqual(first.Key, changed!.Key);
    }

    /// <summary>A loose model and an archive entry with the same bytes are one model.</summary>
    [Fact]
    public void GetOrConvert_ForALooseFileAndAnArchiveEntryOfTheSameBytes_SharesTheEntry()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]).WithArchiveEntry(Twin, [1, 2, 3]);
        var converter = new CountingConverter();
        var cache = Build(assets);

        var loose = cache.GetOrConvert(Model, NoClips, converter.Convert);
        var packed = cache.GetOrConvert(Twin, NoClips, converter.Convert);

        Assert.Equal(1, converter.Calls);
        Assert.Equal(loose!.Key, packed!.Key);
    }

    [Fact]
    public void GetOrConvert_WithNoProject_ConvertsAndWritesNothing()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var converter = new CountingConverter();

        var entry = Build(assets, false).GetOrConvert(Model, NoClips, converter.Convert);

        Assert.NotNull(entry);
        Assert.Null(entry.File);
        Assert.Equal(converter.GlbFor([1, 2, 3]), entry.ReadBytes());
        Assert.False(assets.Files.Directory.Exists(PreviewDir));
    }

    /// <summary>
    ///     The indexer revisits the ignore file only when it saves a snapshot, so a project whose
    ///     snapshot is current would never have the new folder listed. The cache asks before its
    ///     first write instead - once, however many models follow, and not at all for a hit.
    /// </summary>
    [Fact]
    public void GetOrConvert_BeforeItsFirstWrite_HasTheCacheFolderIgnoredOnce()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]).With(Clip, [9]);
        var location = new FixedLocation(PreviewDir);
        var cache = Build(assets, location: location);

        cache.GetOrConvert(Model, NoClips, new CountingConverter().Convert);
        cache.GetOrConvert(Model, OneClip, new CountingConverter().Convert);

        Assert.Equal(1, location.IgnoreRequests);

        var later = new FixedLocation(PreviewDir);
        Build(assets, location: later).GetOrConvert(Model, NoClips, new CountingConverter().Convert);

        Assert.Equal(0, later.IgnoreRequests);
    }

    [Fact]
    public void GetOrConvert_WhenTheConverterThrows_PassesItOnAndKeepsNothing()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var cache = Build(assets);

        Assert.Throws<InvalidDataException>(() =>
            cache.GetOrConvert(Model, NoClips, (_, _) => throw new InvalidDataException("not a model")));

        Assert.Empty(Entries(assets));

        var converter = new CountingConverter();
        var entry = cache.GetOrConvert(Model, NoClips, converter.Convert);

        Assert.Equal(1, converter.Calls);
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task GetOrConvert_AskedForOneModelFromTwoThreads_ConvertsOnce()
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var cache = Build(assets);
        var calls = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        PreviewModelConversion Slow(byte[] model, IReadOnlyList<PreviewClipSource> clips)
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return new PreviewModelConversion([.. model], [], []);
        }

        var first = Task.Run(() => cache.GetOrConvert(Model, NoClips, Slow), TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        var second = Task.Run(() => cache.GetOrConvert(Model, NoClips, Slow), TestContext.Current.CancellationToken);

        // Give the second request the time to reach the conversion if nothing holds it back.
        await Task.Delay(200, TestContext.Current.CancellationToken);
        release.Set();
        var entries = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.Equal(entries[0]!.Key, entries[1]!.Key);
    }

    /// <summary>
    ///     A file under the entry's name that is not a whole entry of this format - cut short, written
    ///     over, or laid out by a format version this server does not write - is a miss: converted
    ///     again and written over, never an error.
    /// </summary>
    [Theory]
    [InlineData("garbage")]
    [InlineData("truncated")]
    [InlineData("other-version")]
    [InlineData("empty")]
    public void GetOrConvert_WithADamagedOrForeignEntry_ConvertsAgainAndWritesOverIt(string damage)
    {
        var assets = new DiskAssets().With(Model, [1, 2, 3]);
        var converter = new CountingConverter();
        var first = Build(assets).GetOrConvert(Model, NoClips, converter.Convert);
        var whole = assets.Files.File.ReadAllBytes(first!.File!);
        var damaged = damage switch
        {
            "garbage" => "{ not an entry at all, but longer than a header }"u8.ToArray(),
            "truncated" => whole[..^2],
            // The format version sits behind the four magic bytes.
            "other-version" => [.. whole[..4], 0x63, 0x00, .. whole[6..]],
            _ => []
        };
        assets.Files.File.WriteAllBytes(first.File!, damaged);

        var second = Build(assets).GetOrConvert(Model, NoClips, converter.Convert);

        Assert.Equal(2, converter.Calls);
        Assert.Equal(converter.GlbFor([1, 2, 3]), second!.ReadBytes());
        Assert.Equal(whole, assets.Files.File.ReadAllBytes(first.File!));
    }

    /// <summary>Converts to something that names its inputs, and counts how often it was asked.</summary>
    private sealed class CountingConverter
    {
        private int _calls;

        public int Calls => _calls;

        /// <summary>What the conversion says about its clips.</summary>
        public IReadOnlyList<PreviewClipNote> Notes { get; init; } = [];

        public PreviewModelConversion Convert(byte[] model, IReadOnlyList<PreviewClipSource> clips)
        {
            Interlocked.Increment(ref _calls);
            return new PreviewModelConversion(
                GlbFor(model, [.. clips.Select(c => c.Bytes)]),
                [.. clips.Select(c => Path.GetFileNameWithoutExtension(c.Name))],
                Notes);
        }

        public byte[] GlbFor(byte[] model, params byte[][] clips)
        {
            return [0x67, 0x6C, 0x54, 0x46, .. model, .. clips.SelectMany(c => c)];
        }
    }

    private sealed class FixedLocation(string? directory) : IPreviewCacheLocation
    {
        public int IgnoreRequests { get; private set; }

        public string? Directory => directory;

        public void EnsureIgnored()
        {
            IgnoreRequests++;
        }
    }

    /// <summary>Loose files on a mock disk and entries in an archive, with every read counted.</summary>
    private sealed class DiskAssets : IGameAssetResolver
    {
        private readonly Dictionary<string, byte[]> _archive = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _reads = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _clock = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public MockFileSystem Files { get; } = new();

        public GameAssetTiers Tiers => new(1, false, false, 1);

        public GameAssetLocation? Locate(string gameRelativePath)
        {
            var loose = "/mod/" + gameRelativePath;
            if (Files.File.Exists(loose))
                return new GameAssetLocation(gameRelativePath, loose, GameAssetTier.Workspace);

            return _archive.ContainsKey(gameRelativePath)
                ? new GameAssetLocation(gameRelativePath, gameRelativePath, GameAssetTier.Archive)
                : null;
        }

        public byte[]? Read(string gameRelativePath)
        {
            return Locate(gameRelativePath) is { } location ? Read(location) : null;
        }

        public byte[]? Read(GameAssetLocation location)
        {
            lock (_reads)
            {
                _reads[location.RequestedPath] = Reads(location.RequestedPath) + 1;
            }

            return location.Tier == GameAssetTier.Archive
                ? _archive.GetValueOrDefault(location.ResolvedPath)
                : Files.File.ReadAllBytes(location.ResolvedPath);
        }

        public int Reads(string path)
        {
            return _reads.GetValueOrDefault(path);
        }

        /// <summary>Writes a loose file; each write is a minute later than the one before.</summary>
        public DiskAssets With(string path, byte[] bytes)
        {
            var file = "/mod/" + path;
            Files.Directory.CreateDirectory(Files.Path.GetDirectoryName(file)!);
            Files.File.WriteAllBytes(file, bytes);
            _clock = _clock.AddMinutes(1);
            Files.File.SetLastWriteTimeUtc(file, _clock);
            return this;
        }

        /// <summary>Writes over a loose file and leaves its write time as it was, as a copy does.</summary>
        public DiskAssets WithSameStamp(string path, byte[] bytes)
        {
            var file = "/mod/" + path;
            var written = Files.File.GetLastWriteTimeUtc(file);
            Files.File.WriteAllBytes(file, bytes);
            Files.File.SetLastWriteTimeUtc(file, written);
            return this;
        }

        public DiskAssets WithArchiveEntry(string path, byte[] bytes)
        {
            _archive[path] = bytes;
            return this;
        }
    }
}

/// <summary>For the tests that are not about which clips play: every offered clip does.</summary>
file static class EveryClipPlays
{
    public static PreviewCachedModel? GetOrConvert(this PreviewModelCache cache, string modelPath,
        IReadOnlyList<(string Name, string Path)> clips, PreviewModelConverter convert)
    {
        return cache.GetOrConvert(modelPath, clips, convert,
            (_, offered) => [.. offered.Select(_ => new PreviewClipVerdict(true, null))]);
    }
}
