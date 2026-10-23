// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using MessagePack;

namespace PG.StarWarsGame.LSP.Assets.Serialization;

/// <summary>A loaded icon pack: PNGs by mega texture directory name, plus its provenance.</summary>
public sealed record IconPack(
    ImmutableDictionary<string, byte[]> Icons,
    string SourceManifestHash,
    DateTimeOffset BuiltAt)
{
    public static IconPack Empty { get; } = new(
        ImmutableDictionary.Create<string, byte[]>(StringComparer.OrdinalIgnoreCase),
        string.Empty,
        DateTimeOffset.MinValue);
}

/// <summary>
///     Reads and writes the icon sidecar that ships beside a baseline.
/// </summary>
/// <remarks>
///     Mirrors <see cref="BaselineSerializer" />'s contract - a stale or unreadable pack deserializes
///     to <see langword="null" /> rather than throwing, so a bad sidecar degrades icons to the
///     embedded fallback instead of taking the server down with it. Unlike the baseline this is not
///     gzipped: PNG payloads are already compressed.
/// </remarks>
public static class IconPackSerializer
{
    /// <summary>
    ///     Path of the sidecar belonging to <paramref name="baselinePath" />. Follows the same
    ///     sibling-file convention as the existing <c>.manifest.json</c>.
    /// </summary>
    public static string SidecarPathFor(string baselinePath)
    {
        return baselinePath + ".icons";
    }

    /// <summary>
    ///     The bytes to write over <paramref name="previous" />: those bytes themselves when they hold
    ///     the same icons, their build time included; otherwise a new build. Like
    ///     <see cref="BaselineSerializer.SerializeOver" />, so a rebuild that changes nothing changes no byte.
    /// </summary>
    public static byte[] SerializeOver(
        IReadOnlyDictionary<string, byte[]> icons,
        string sourceManifestHash,
        DateTimeOffset builtAt,
        byte[]? previous)
    {
        if (previous is null || Deserialize(previous) is not { } earlier)
            return Serialize(icons, sourceManifestHash, builtAt);
        var same = Serialize(icons, sourceManifestHash, earlier.BuiltAt);
        return same.AsSpan().SequenceEqual(previous) ? previous : Serialize(icons, sourceManifestHash, builtAt);
    }

    public static byte[] Serialize(
        IReadOnlyDictionary<string, byte[]> icons,
        string sourceManifestHash,
        DateTimeOffset builtAt)
    {
        ArgumentNullException.ThrowIfNull(icons);

        var dto = new SerializedIconPack
        {
            // Ordinal order, so two builds of the same icons are one file.
            Icons =
            [
                .. icons.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new SerializedIcon { Name = kv.Key, Png = kv.Value })
            ],
            SchemaVersion = SerializedIconPack.CurrentSchemaVersion,
            SourceManifestHash = sourceManifestHash,
            BuiltAtMs = builtAt.ToUnixTimeMilliseconds()
        };

        return MessagePackSerializer.Serialize(dto);
    }

    public static IconPack? Deserialize(byte[] data)
    {
        try
        {
            var dto = MessagePackSerializer.Deserialize<SerializedIconPack>(data);
            if (dto.SchemaVersion != SerializedIconPack.CurrentSchemaVersion)
                return null;

            var icons = (dto.Icons ?? []).ToImmutableDictionary(
                i => i.Name, i => i.Png, StringComparer.OrdinalIgnoreCase);

            return new IconPack(
                icons,
                dto.SourceManifestHash ?? string.Empty,
                DateTimeOffset.FromUnixTimeMilliseconds(dto.BuiltAtMs));
        }
        catch
        {
            return null;
        }
    }
}