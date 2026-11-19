// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>One clip offered to a conversion: the name it was asked for by, and its file's bytes.</summary>
public sealed record PreviewClipSource(string Name, byte[] Bytes);

/// <summary>How a clip offered to a conversion fared, when that is worth saying.</summary>
public static class PreviewClipNoteKind
{
    /// <summary>Baked in, across a skeleton that differs from the one it was authored against.</summary>
    public const string DiffersButPlays = "differs";

    /// <summary>Left out: it drives a bone the model does not have.</summary>
    public const string CannotPlay = "cannot-play";

    /// <summary>Left out: the file is not a readable animation.</summary>
    public const string Unreadable = "unreadable";
}

/// <summary>
///     What a conversion had to say about one clip. Kept beside the converted model, without the
///     model's name - one entry answers for every model of the same bytes - so the same thing is
///     said again when the entry is served from the cache.
/// </summary>
/// <param name="Clip">The clip, by the name it was asked for.</param>
/// <param name="Kind">One of <see cref="PreviewClipNoteKind" />.</param>
/// <param name="Reason">The reader's or the pairing's own words.</param>
public sealed record PreviewClipNote(string Clip, string Kind, string Reason);

/// <summary>What a conversion produced: the GLB, the clips baked into it, and what it said of the rest.</summary>
public sealed record PreviewModelConversion(
    byte[] Glb,
    IReadOnlyList<string> Animations,
    IReadOnlyList<PreviewClipNote> Notes);

/// <summary>Turns a model's bytes and the clips offered with it into a GLB.</summary>
public delegate PreviewModelConversion PreviewModelConverter(byte[] model, IReadOnlyList<PreviewClipSource> clips);

/// <summary>What is known of one offered clip against one model before anything is converted.</summary>
/// <param name="Plays">Whether the conversion bakes the clip into the model.</param>
/// <param name="WhyNot">What to say of a clip that is not baked. Null for one that is.</param>
public sealed record PreviewClipVerdict(bool Plays, PreviewClipNote? WhyNot);

/// <summary>
///     Judges each offered clip against a model, in the order offered, by the rule the converter
///     itself keeps or drops a clip by.
/// </summary>
public delegate IReadOnlyList<PreviewClipVerdict> PreviewClipJudge(byte[] model,
    IReadOnlyList<PreviewClipSource> clips);

/// <summary>A converted model, in the preview cache or - with no project to hold one - in memory.</summary>
public sealed class PreviewCachedModel
{
    private readonly Func<Stream> _open;

    internal PreviewCachedModel(string key, IReadOnlyList<string> animations, IReadOnlyList<PreviewClipNote> notes,
        long length, string? file, Func<Stream> open)
    {
        Key = key;
        Animations = animations;
        Notes = notes;
        Length = length;
        File = file;
        _open = open;
    }

    /// <summary>The content key: the same for every model and clip set that converts to this GLB.</summary>
    public string Key { get; }

    /// <summary>The clips baked in, by the name the GLB exposes.</summary>
    public IReadOnlyList<string> Animations { get; }

    /// <summary>What the conversion said about the clips offered, for the log.</summary>
    public IReadOnlyList<PreviewClipNote> Notes { get; }

    /// <summary>The GLB's size in bytes.</summary>
    public long Length { get; }

    /// <summary>The cache entry's file, or null when the model is held in memory.</summary>
    public string? File { get; }

    /// <summary>
    ///     The GLB, as a stream of exactly <see cref="Length" /> bytes the caller disposes. For an
    ///     entry on disk it reads the file's payload section, so the GLB is never held whole.
    /// </summary>
    public Stream OpenPayload()
    {
        return _open();
    }

    /// <summary>The same entry, saying these notes as well: what this request offered that is not in it.</summary>
    internal PreviewCachedModel With(IReadOnlyList<PreviewClipNote> more)
    {
        return more.Count == 0
            ? this
            : new PreviewCachedModel(Key, Animations, [.. Notes, .. more], Length, File, _open);
    }

    /// <summary>The GLB's bytes, for a caller that wants them whole.</summary>
    public byte[] ReadBytes()
    {
        using var payload = OpenPayload();
        var bytes = new byte[Length];
        payload.ReadExactly(bytes);
        return bytes;
    }
}

/// <summary>Converted models, kept for the next request.</summary>
public interface IPreviewModelCache
{
    /// <summary>
    ///     The converted model for <paramref name="modelPath" /> and the clips offered with it,
    ///     converting on a miss. Null when the model resolves nowhere.
    /// </summary>
    /// <param name="modelPath">The model's game-relative path.</param>
    /// <param name="clips">
    ///     Each clip offered, by the name it was asked for and its game-relative path, in request
    ///     order. One that resolves nowhere is left out.
    /// </param>
    /// <param name="convert">Runs on a miss. What it throws reaches the caller and nothing is kept.</param>
    /// <param name="judge">
    ///     Says which of the offered clips the conversion would bake. Only those are converted with
    ///     and only those are part of the entry's key, so two requests that offer different clips
    ///     and bake the same ones share an entry.
    /// </param>
    PreviewCachedModel? GetOrConvert(string modelPath, IReadOnlyList<(string Name, string Path)> clips,
        PreviewModelConverter convert, PreviewClipJudge judge);

    /// <summary>
    ///     Empties the cache folder - the entries, and whatever else was left in it - and returns how
    ///     many entries were removed. A file that cannot be removed is left and logged.
    /// </summary>
    int Clear();
}

/// <summary>Where the preview cache of the open project lives.</summary>
public interface IPreviewCacheLocation
{
    /// <summary>The cache directory, or null when the workspace has no project to hold one.</summary>
    string? Directory { get; }

    /// <summary>
    ///     Lists the cache folder in the project's <c>.aetswg/.gitignore</c>. Asked for before the
    ///     first write, so a project whose index snapshot is current - and whose ignore file is
    ///     therefore never revisited by the indexer - still has the folder ignored.
    /// </summary>
    void EnsureIgnored();
}

/// <summary>The root project's <c>.aetswg/preview/</c>.</summary>
public sealed class ProjectPreviewCacheLocation(
    IModProjectReloadService projects,
    IProjectIndexCache indexCache) : IPreviewCacheLocation
{
    public string? Directory => RootProject() is { } pgproj
        ? ProjectIndexLocator.GetAetswgDirectory(pgproj) + "/" + PreviewModelCache.DirectoryName
        : null;

    public void EnsureIgnored()
    {
        if (RootProject() is { } pgproj)
            indexCache.EnsureGitHygiene(pgproj);
    }

    private string? RootProject()
    {
        return projects.LastWorkspaceConfig?.Layers
            .OrderByDescending(l => l.Rank)
            .FirstOrDefault()?.ProjectPath;
    }
}

/// <summary>
///     The preview cache on disk: one file per entry, <c>.aetswg/preview/&lt;key&gt;.aetpm</c> under the
///     root project, kept across sessions and never pruned - it holds what the project converts.
/// </summary>
/// <remarks>
///     <para>
///         <strong>Keyed by content.</strong> The key is the converter's version, the model's bytes
///         and, in request order, the name and bytes of each offered clip THAT PLAYS on the model -
///         the clips baked into the GLB, never one that was offered and dropped. A unit offers a
///         hardpoint's model the hull's clips and the background build offers it its own; where
///         none plays, both find one entry. What a model is CALLED is not
///         part of it, so byte-identical models under different file names are one entry - 153 of
///         514 models in one measured mod. A clip's name is part of it because the GLB exposes the
///         clip under that name.
///     </para>
///     <para>
///         <strong>A file is hashed once.</strong> <see cref="IPreviewAssetFingerprints" /> answers
///         for a loose file it has already hashed, so a request for an unchanged model costs a
///         file-system query, not a read - MEASURED: 35 ms to read and 8 ms to hash a 149 MB model,
///         against 0.1 ms for the query - and forgets a file the watcher reports, whatever its
///         timestamps say. An archive entry has no write time to ask for; its bytes are read and
///         hashed on every request.
///     </para>
///     <para>
///         <strong>One file, written once.</strong> The clip names, the conversion's notes and the
///         GLB are sections of one file (<see cref="PreviewCacheEntryFormat" />), moved into place
///         whole. A file that is not a whole entry of the format this server writes is a miss, and
///         is converted again and written over - never an error.
///     </para>
///     <para>
///         <strong>Nothing is held in memory but the hash table.</strong> A hit reads the entry's
///         header and metadata; the GLB stays in the file until it is streamed out.
///     </para>
/// </remarks>
public sealed class PreviewModelCache(
    IPreviewAssetFingerprints fingerprints,
    IFileHelper fileHelper,
    IPreviewCacheLocation cacheLocation,
    ILogger<PreviewModelCache> logger,
    int converterVersion = PreviewModelCache.ConverterVersion) : IPreviewModelCache
{
    /// <summary>
    ///     Part of every key. Raised whenever the same input would convert to a different GLB: a
    ///     change to the model or animation readers, to the exporter, or to which clips are kept.
    /// </summary>
    public const int ConverterVersion = 1;

    /// <summary>The cache directory's name under <c>.aetswg/</c>.</summary>
    public const string DirectoryName = "preview";

    private static readonly JsonSerializerOptions MetadataJson = new(JsonSerializerDefaults.Web);

    /// <summary>Conversions under way, so two requests for one key wait for the same one.</summary>
    private readonly ConcurrentDictionary<string, Lazy<PreviewCachedModel>> _converting = new(StringComparer.Ordinal);

    /// <summary>
    ///     What the judge said of each model and clip, by the hashes of their bytes: whether the
    ///     clip plays, and the kind and reason of the note when it does not. Asked once per pair and
    ///     session; a changed file has another hash. Small - a flag and a sentence per pair served.
    /// </summary>
    private readonly ConcurrentDictionary<(string Model, string Clip), (bool Plays, string? Kind, string? Reason)>
        _verdicts = new();

    /// <summary>The cache directories already made sure of in the ignore file, this session.</summary>
    private readonly ConcurrentDictionary<string, bool> _ignored = new(StringComparer.OrdinalIgnoreCase);

    public PreviewCachedModel? GetOrConvert(string modelPath, IReadOnlyList<(string Name, string Path)> clips,
        PreviewModelConverter convert, PreviewClipJudge judge)
    {
        var model = fingerprints.Identify(modelPath);
        if (model is null)
            return null;

        var named = new List<(string Name, PreviewAssetIdentity Source)>();
        foreach (var (name, path) in clips)
            if (fingerprints.Identify(path) is { } clip)
                named.Add((name, clip));

        // Only what is baked is what the GLB is. A unit offers a hardpoint's model the hull's
        // clips and the background build offers it its own; where none of either plays on it,
        // both mean the same GLB and must find the same entry.
        var dropped = new List<PreviewClipNote>();
        var offered = Playing(model, named, judge, dropped);

        var key = KeyOf(model, offered);
        var directory = cacheLocation.Directory;

        // No project, so nowhere to keep it: converted for this request and handed over.
        if (directory is null)
            return InMemory(key, Convert(model, offered, convert)).With(dropped);

        if (TryLoad(directory, key) is { } kept)
            return kept.With(dropped);

        var pending = _converting.GetOrAdd(key,
            _ => new Lazy<PreviewCachedModel>(() =>
                TryLoad(directory, key) ?? ConvertAndKeep(directory, key, model, offered, convert)));
        try
        {
            return pending.Value.With(dropped);
        }
        finally
        {
            _converting.TryRemove(new KeyValuePair<string, Lazy<PreviewCachedModel>>(key, pending));
        }
    }

    /// <summary>
    ///     The offered clips that play on the model, in the order offered; the notes of the ones
    ///     that do not go into <paramref name="dropped" />. The judge is asked once per model and
    ///     clip - reading the model for it - and its answer kept by their hashes.
    /// </summary>
    private List<(string Name, PreviewAssetIdentity Source)> Playing(PreviewAssetIdentity model,
        List<(string Name, PreviewAssetIdentity Source)> offered, PreviewClipJudge judge,
        List<PreviewClipNote> dropped)
    {
        if (offered.Count == 0)
            return offered;

        var unjudged = offered.Where(c => !_verdicts.ContainsKey((model.Hash, c.Source.Hash))).ToList();
        if (unjudged.Count > 0)
        {
            var modelBytes = model.Bytes() ??
                             throw new IOException($"'{model.Location.ResolvedPath}' can no longer be read.");

            var sources = new List<PreviewClipSource>();
            var hashes = new List<string>();
            foreach (var (name, source) in unjudged)
                if (source.Bytes() is { } bytes)
                {
                    sources.Add(new PreviewClipSource(name, bytes));
                    hashes.Add(source.Hash);
                }

            var verdicts = judge(modelBytes, sources);
            for (var i = 0; i < hashes.Count && i < verdicts.Count; i++)
                _verdicts[(model.Hash, hashes[i])] =
                    (verdicts[i].Plays, verdicts[i].WhyNot?.Kind, verdicts[i].WhyNot?.Reason);
        }

        var playing = new List<(string Name, PreviewAssetIdentity Source)>();
        foreach (var clip in offered)
        {
            // No verdict: it could not be read just now, which is a clip that resolves nowhere.
            if (!_verdicts.TryGetValue((model.Hash, clip.Source.Hash), out var verdict))
                continue;

            if (verdict.Plays)
                playing.Add(clip);
            else if (verdict.Kind is not null)
                dropped.Add(new PreviewClipNote(clip.Name, verdict.Kind, verdict.Reason ?? string.Empty));
        }

        return playing;
    }

    public int Clear()
    {
        var fs = fileHelper.FileSystem;
        var directory = cacheLocation.Directory;
        if (directory is null || !fs.Directory.Exists(directory))
            return 0;

        var removed = 0;
        foreach (var file in fs.Directory.GetFiles(directory))
            try
            {
                fs.File.Delete(file);
                if (file.EndsWith(PreviewCacheEntryFormat.Extension, StringComparison.OrdinalIgnoreCase))
                    removed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Being read for a preview right now. It is valid where it stands, and goes with the
                // next clear.
                logger.LogWarning(e, "Could not remove '{File}' from the preview cache", file);
            }

        return removed;
    }

    /// <summary>The entry on disk for a key, or null when there is none that can be served whole.</summary>
    private PreviewCachedModel? TryLoad(string directory, string key)
    {
        var fs = fileHelper.FileSystem;
        var path = EntryPath(directory, key);
        if (!fs.File.Exists(path))
            return null;

        try
        {
            using var file = fs.File.OpenRead(path);
            var layout = PreviewCacheEntryFormat.TryReadLayout(file, file.Length);
            if (layout?.Section(PreviewCacheEntryFormat.MetadataSection) is not { } metadataAt
                || layout.Section(PreviewCacheEntryFormat.PayloadSection) is not { } payloadAt)
                return null;

            var raw = new byte[metadataAt.Length];
            file.Position = metadataAt.Offset;
            file.ReadExactly(raw);

            var metadata = JsonSerializer.Deserialize<EntryMetadata>(raw, MetadataJson);
            if (metadata?.Animations is null || metadata.Notes is null)
                return null;

            return new PreviewCachedModel(key, metadata.Animations, metadata.Notes, payloadAt.Length, path,
                () => new SectionStream(fs.File.OpenRead(path), payloadAt.Offset, payloadAt.Length));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Converted again and written over rather than served in part.
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug(e, "Preview cache entry {Key} is unreadable; converting again", key);
            return null;
        }
    }

    private PreviewCachedModel ConvertAndKeep(string directory, string key, PreviewAssetIdentity model,
        List<(string Name, PreviewAssetIdentity Source)> offered, PreviewModelConverter convert)
    {
        var fs = fileHelper.FileSystem;
        var conversion = Convert(model, offered, convert);
        var path = EntryPath(directory, key);

        try
        {
            EnsureIgnored(directory);

            var metadata = JsonSerializer.SerializeToUtf8Bytes(
                new EntryMetadata(conversion.Animations, conversion.Notes), MetadataJson);

            AtomicFile.Write(fs, path, stream => PreviewCacheEntryFormat.Write(stream,
            [
                (PreviewCacheEntryFormat.MetadataSection, metadata),
                (PreviewCacheEntryFormat.PayloadSection, conversion.Glb)
            ]));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The cache is an accelerator: a conversion that cannot be kept is still an answer.
            logger.LogWarning(e, "Could not keep the converted model {Key} in the preview cache at '{Path}'", key,
                path);
            return InMemory(key, conversion);
        }

        // Read back through the same door a later request uses, so what is handed out now is
        // what the file holds.
        return TryLoad(directory, key) ?? InMemory(key, conversion);
    }

    private static PreviewCachedModel InMemory(string key, PreviewModelConversion conversion)
    {
        return new PreviewCachedModel(key, conversion.Animations, conversion.Notes, conversion.Glb.LongLength, null,
            () => new MemoryStream(conversion.Glb, false));
    }

    private static PreviewModelConversion Convert(PreviewAssetIdentity model,
        List<(string Name, PreviewAssetIdentity Source)> offered, PreviewModelConverter convert)
    {
        var modelBytes = model.Bytes() ??
                         throw new IOException($"'{model.Location.ResolvedPath}' can no longer be read.");

        var clips = new List<PreviewClipSource>();
        foreach (var (name, source) in offered)
            if (source.Bytes() is { } bytes)
                clips.Add(new PreviewClipSource(name, bytes));

        return convert(modelBytes, clips);
    }

    private string KeyOf(PreviewAssetIdentity model, List<(string Name, PreviewAssetIdentity Source)> offered)
    {
        var text = new StringBuilder();
        text.Append('v').Append(converterVersion).Append('\n').Append(model.Hash).Append('\n');
        foreach (var (name, source) in offered)
            text.Append(name).Append('\t').Append(source.Hash).Append('\n');

        return PreviewAssetFingerprints.HashOf(Encoding.UTF8.GetBytes(text.ToString()));
    }

    /// <summary>Has the cache folder listed in the ignore file, once per directory and session.</summary>
    private void EnsureIgnored(string directory)
    {
        if (!_ignored.TryAdd(directory, true))
            return;

        try
        {
            cacheLocation.EnsureIgnored();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Tried again in the next session; the entry is written either way.
            logger.LogWarning(e, "Could not list the preview cache in the ignore file beside '{Path}'", directory);
        }
    }

    private static string EntryPath(string directory, string key)
    {
        return directory + "/" + key + PreviewCacheEntryFormat.Extension;
    }

    /// <summary>The metadata section of an entry.</summary>
    private sealed record EntryMetadata(IReadOnlyList<string>? Animations, IReadOnlyList<PreviewClipNote>? Notes);

    /// <summary>One section of a file as a read-only stream of its own, which closes the file with it.</summary>
    private sealed class SectionStream : Stream
    {
        private readonly Stream _file;
        private long _left;

        public SectionStream(Stream file, long offset, long length)
        {
            _file = file;
            _file.Position = offset;
            _left = length;
            Length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length { get; }

        public override long Position
        {
            get => Length - _left;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (_left == 0)
                return 0;

            var read = _file.Read(buffer[..(int)Math.Min(buffer.Length, _left)]);
            _left -= read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_left == 0)
                return 0;

            var read = await _file.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _left)], cancellationToken);
            _left -= read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _file.Dispose();

            base.Dispose(disposing);
        }
    }
}
