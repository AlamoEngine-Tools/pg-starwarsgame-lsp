// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Compression;
using MessagePack;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Serialization;

/// <remarks>
///     Reproducible: every set and map is written in ordinal order, since a hashed collection
///     enumerates in an order .NET randomises per process, and <see cref="SerializeOver" /> keeps a
///     previous file whose content is the same, so a rebuild that changes nothing changes no byte.
/// </remarks>
public static class BaselineSerializer
{
    /// <summary>
    ///     The bytes to write over <paramref name="previous" />: those bytes themselves when they hold
    ///     the same baseline, their build time included; otherwise a new build.
    /// </summary>
    public static byte[] SerializeOver(BaselineIndex baseline, byte[]? previous)
    {
        if (previous is null || Deserialize(previous) is not { } earlier) return Serialize(baseline);
        var same = Serialize(baseline with { BuiltAt = earlier.BuiltAt });
        return same.AsSpan().SequenceEqual(previous) ? previous : Serialize(baseline);
    }

    public static byte[] Serialize(BaselineIndex baseline)
    {
        var dto = new SerializedBaseline
        {
            Symbols = [.. baseline.Symbols.Values.OrderBy(s => s.Id, StringComparer.Ordinal)],
            BuiltAtMs = baseline.BuiltAt.ToUnixTimeMilliseconds(),
            SourceManifestHash = baseline.SourceManifestHash,
            DynamicEnumValues = ToSerializedArray(baseline.DynamicEnumValues),
            HardcodedEnumValues = ToSerializedArray(baseline.HardcodedEnumValues),
            FileTypeMap = ToSerializedArray(baseline.FileTypeMap),
            GroupMemberships = ToSerializedGroupMemberships(baseline.GroupMemberships),
            AssetFiles = [.. baseline.AssetFiles.Order(StringComparer.Ordinal)],
            ModelBones = ToSerializedArray(baseline.ModelBones),
            ModelTextures = ToSerializedArray(baseline.ModelTextures),
            ParticleSystems = [.. baseline.ParticleSystems.Order(StringComparer.Ordinal)],
            ModelProxies = ToSerializedArray(baseline.ModelProxies),
            RegistryLists = ToSerializedArray(baseline.RegistryLists),
            ObjectTags = ToSerializedObjectTags(baseline.ObjectTags),
            SchemaVersion = SerializedBaseline.CurrentSchemaVersion
        };
        var msgpack = MessagePackSerializer.Serialize(dto);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal))
        {
            gz.Write(msgpack);
        }

        return ms.ToArray();
    }

    public static BaselineIndex? Deserialize(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            gz.CopyTo(decompressed);
            var dto = MessagePackSerializer.Deserialize<SerializedBaseline>(decompressed.ToArray());
            return dto.SchemaVersion != SerializedBaseline.CurrentSchemaVersion ? null : FromDto(dto);
        }
        catch
        {
            return null;
        }
    }

    private static BaselineIndex FromDto(SerializedBaseline dto)
    {
        var builtAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.BuiltAtMs);
        var symbols = dto.Symbols.ToImmutableDictionary(s => s.Id);
        var enums = FromSerializedArray(dto.DynamicEnumValues);
        var hardcoded = FromSerializedArray(dto.HardcodedEnumValues);
        var fileTypeMap = FromSerializedArray(dto.FileTypeMap);
        var groupMemberships = FromSerializedGroupMemberships(dto.GroupMemberships);
        var assetFiles = dto.AssetFiles.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract - deserialized data; the declared type is not the payload's contract
        var modelBones = (dto.ModelBones ?? []).ToImmutableDictionary(
            e => e.Name, e => e.Values.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract - deserialized data; the declared type is not the payload's contract
        var modelTextures = (dto.ModelTextures ?? []).ToImmutableDictionary(
            e => e.Name, e => e.Values.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract - deserialized data; the declared type is not the payload's contract
        var objectTags = FromSerializedObjectTags(dto.ObjectTags ?? []);
        return new BaselineIndex(symbols, builtAt, dto.SourceManifestHash, enums, hardcoded, fileTypeMap)
        {
            GroupMemberships = groupMemberships,
            AssetFiles = assetFiles,
            ModelBones = modelBones,
            ModelTextures = modelTextures,
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract - deserialized data; the declared type is not the payload's contract
            ParticleSystems = (dto.ParticleSystems ?? []).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract - deserialized data; the declared type is not the payload's contract
            ModelProxies = (dto.ModelProxies ?? []).ToImmutableDictionary(
                e => e.Name, e => e.Values.ToImmutableArray(), StringComparer.OrdinalIgnoreCase),
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract - deserialized data; the declared type is not the payload's contract
            RegistryLists = (dto.RegistryLists ?? []).ToImmutableDictionary(
                e => e.Name, e => e.Values.ToImmutableArray(), StringComparer.OrdinalIgnoreCase),
            ObjectTags = objectTags
        };
    }

    private static SerializedObjectTags[] ToSerializedObjectTags(
        ImmutableDictionary<string, ImmutableArray<BaselineTag>> dict)
    {
        return
        [
            .. dict.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new SerializedObjectTags { Name = kv.Key, Tags = [.. kv.Value] })
        ];
    }

    private static ImmutableDictionary<string, ImmutableArray<BaselineTag>> FromSerializedObjectTags(
        SerializedObjectTags[] arr)
    {
        return arr.ToImmutableDictionary(
            e => e.Name,
            e => e.Tags.ToImmutableArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static SerializedEnumValues[] ToSerializedArray(
        ImmutableDictionary<string, ImmutableArray<string>> dict)
    {
        return
        [
            .. dict.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new SerializedEnumValues { Name = kv.Key, Values = [.. kv.Value] })
        ];
    }

    private static ImmutableDictionary<string, ImmutableArray<string>> FromSerializedArray(
        SerializedEnumValues[] arr)
    {
        return arr.ToImmutableDictionary(e => e.Name, e => e.Values.ToImmutableArray());
    }

    private static SerializedGroupMemberships[] ToSerializedGroupMemberships(
        ImmutableDictionary<string, ImmutableArray<GroupMembership>> dict)
    {
        return
        [
            .. dict.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new SerializedGroupMemberships { GroupKey = kv.Key, Members = [.. kv.Value] })
        ];
    }

    private static ImmutableDictionary<string, ImmutableArray<GroupMembership>> FromSerializedGroupMemberships(
        SerializedGroupMemberships[] arr)
    {
        return arr.ToImmutableDictionary(
            e => e.GroupKey,
            e => e.Members.ToImmutableArray(),
            StringComparer.OrdinalIgnoreCase);
    }
}