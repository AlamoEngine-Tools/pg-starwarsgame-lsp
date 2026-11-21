// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Hashing;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>One resolved asset: where it is, the hash of its bytes, and how to get them.</summary>
public sealed record PreviewAssetIdentity(GameAssetLocation Location, string Hash, Func<byte[]?> Bytes);

/// <summary>
///     What the preview knows about the files it has served, so an unchanged one is neither read
///     nor sent again and a changed one always is.
/// </summary>
public interface IPreviewAssetFingerprints
{
    /// <summary>
    ///     Raised after <see cref="Forget" />, once per call: an open preview asks for its assets
    ///     again, and gets the ones that changed.
    /// </summary>
    event Action? Changed;

    /// <summary>
    ///     Where a path resolves and the hash of what is there, or null when it resolves nowhere.
    ///     A loose file already hashed is not read again while its stamp stands.
    /// </summary>
    PreviewAssetIdentity? Identify(string gameRelativePath);

    /// <summary>
    ///     The same for a path already resolved, so a caller that needs the location for more than
    ///     this does not resolve it twice. Null when it can no longer be read.
    /// </summary>
    PreviewAssetIdentity? Identify(GameAssetLocation location);

    /// <summary>
    ///     Drops everything remembered about these files, whatever their timestamps say. Called
    ///     with the models, clips, textures and archives of a watched-file notification - never
    ///     with a script or a sound, which no preview draws.
    /// </summary>
    void Forget(IReadOnlyCollection<string> paths);
}

/// <summary>
///     The stamps and hashes of the files the preview has served, for this session.
/// </summary>
/// <remarks>
///     <para>
///         <strong>A stamp is not proof.</strong> Write time and length answer for a file's content
///         only until something says otherwise. A copy over a file keeps the SOURCE's write time on
///         Windows - MEASURED: a texture overwritten at 22:06 still showed the 13:33 of the file it
///         was copied from - and every 2048x2048 DXT5 texture has the same length, so a file can be
///         replaced by different bytes under an identical stamp. The file watcher sees that write.
///         Each event it reports is forgotten here by PATH, timestamps unread, and counted into the
///         file's stamp, so the next request reads the file again and the stamp it hands out is new.
///     </para>
///     <para>
///         In memory only, and small: one entry per file served. A new session hashes each file
///         once; MEASURED, 35 ms to read and 8 ms to hash a 149 MB model.
///     </para>
/// </remarks>
public sealed class PreviewAssetFingerprints(IGameAssetResolver assets, IFileHelper fileHelper)
    : IPreviewAssetFingerprints
{
    /// <summary>What the server sends every open preview when <see cref="Changed" /> is raised.</summary>
    public const string ChangedNotification = "aet/previewAssetsChanged";

    /// <summary>The loose files a preview draws from: models, their clips, and textures of every format the game loads.</summary>
    private static readonly string[] DrawnExtensions = [".alo", ".ala", .. GameTextureName.Extensions];

    /// <summary>The hash of each loose file already read, by the stamp it had then.</summary>
    private readonly ConcurrentDictionary<string, Fingerprint> _hashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How often a change was reported for a file.</summary>
    private readonly ConcurrentDictionary<string, int> _reported = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public PreviewAssetIdentity? Identify(string gameRelativePath)
    {
        var location = assets.Locate(gameRelativePath);
        return location is null ? null : Identify(location);
    }

    public PreviewAssetIdentity? Identify(GameAssetLocation location)
    {
        var stamp = StampOf(location);
        if (stamp is not null
            && _hashes.TryGetValue(Key(location.ResolvedPath), out var known)
            && known.Stamp == stamp)
            return new PreviewAssetIdentity(location, known.Hash, () => assets.Read(location));

        var bytes = assets.Read(location);
        if (bytes is null)
            return null;

        var hash = HashOf(bytes);
        if (stamp is not null)
            // The stamp taken BEFORE the read: a file written in between has another stamp on the
            // next request and is read again, where the other order could pin old bytes' hash to
            // the new stamp.
            _hashes[Key(location.ResolvedPath)] = new Fingerprint(stamp, hash);

        return new PreviewAssetIdentity(location, hash, () => bytes);
    }

    public string? StampOf(GameAssetLocation location)
    {
        if (location.Tier == GameAssetTier.Archive)
            return null;

        try
        {
            var info = fileHelper.FileSystem.FileInfo.New(location.ResolvedPath);
            if (!info.Exists)
                return null;

            var reported = _reported.GetValueOrDefault(Key(location.ResolvedPath));
            return string.Create(CultureInfo.InvariantCulture,
                $"{info.LastWriteTimeUtc.Ticks:x}-{info.Length:x}-{reported:x}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                      or NotSupportedException)
        {
            return null;
        }
    }

    public void Forget(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0)
            return;

        foreach (var path in paths)
        {
            var key = Key(path);
            _hashes.TryRemove(key, out _);
            _reported.AddOrUpdate(key, 1, (_, count) => count + 1);
        }

        Changed?.Invoke();
    }

    /// <summary>
    ///     Whether a change to this loose file can change what a preview draws. Only such a file is
    ///     worth forgetting: <see cref="Forget" /> has every open preview ask for its assets again.
    /// </summary>
    public static bool IsDrawn(string path)
    {
        var extension = Path.GetExtension(path);
        return DrawnExtensions.Any(drawn => extension.Equals(drawn, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The hash this class takes of a file's bytes.</summary>
    public static string HashOf(byte[] bytes)
    {
        return Convert.ToHexStringLower(XxHash128.Hash(bytes));
    }

    /// <summary>
    ///     One spelling per file: the resolver hands out a path as the disk or the project spells it,
    ///     the watcher as the client does, and the two differ in separators.
    /// </summary>
    private static string Key(string path)
    {
        return path.Replace('\\', '/');
    }

    private readonly record struct Fingerprint(string Stamp, string Hash);
}
