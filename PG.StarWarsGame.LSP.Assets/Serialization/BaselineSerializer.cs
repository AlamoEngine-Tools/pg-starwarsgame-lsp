// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Compression;
using MessagePack;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Serialization;

public static class BaselineSerializer
{
    public static byte[] Serialize(BaselineIndex baseline)
    {
        var dto = new SerializedBaseline
        {
            Symbols = baseline.Symbols.Values.ToArray(),
            BuiltAtMs = baseline.BuiltAt.ToUnixTimeMilliseconds(),
            SourceManifestHash = baseline.SourceManifestHash,
            DynamicEnumValues = ToSerializedArray(baseline.DynamicEnumValues),
            HardcodedEnumValues = ToSerializedArray(baseline.HardcodedEnumValues),
            FileTypeMap = ToSerializedArray(baseline.FileTypeMap),
            GroupMemberships = ToSerializedGroupMemberships(baseline.GroupMemberships),
            AssetFiles = baseline.AssetFiles.ToArray(),
            ModelBones = ToSerializedArray(baseline.ModelBones),
            ObjectTags = ToSerializedObjectTags(baseline.ObjectTags)
        };
        var msgpack = MessagePackSerializer.Serialize(dto);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal))
        {
            gz.Write(msgpack);
        }

        return ms.ToArray();
    }

    public static BaselineIndex Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        gz.CopyTo(decompressed);
        var dto = MessagePackSerializer.Deserialize<SerializedBaseline>(decompressed.ToArray());
        var builtAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.BuiltAtMs);
        var symbols = dto.Symbols.ToImmutableDictionary(s => s.Id);
        var enums = FromSerializedArray(dto.DynamicEnumValues);
        var hardcoded = FromSerializedArray(dto.HardcodedEnumValues);
        var fileTypeMap = FromSerializedArray(dto.FileTypeMap);
        var groupMemberships = FromSerializedGroupMemberships(dto.GroupMemberships);
        var assetFiles = dto.AssetFiles.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var modelBones = (dto.ModelBones ?? []).ToImmutableDictionary(
            e => e.Name, e => e.Values.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
        var objectTags = FromSerializedObjectTags(dto.ObjectTags ?? []);
        return new BaselineIndex(symbols, builtAt, dto.SourceManifestHash, enums, hardcoded, fileTypeMap)
        {
            GroupMemberships = groupMemberships,
            AssetFiles = assetFiles,
            ModelBones = modelBones,
            ObjectTags = objectTags
        };
    }

    private static SerializedObjectTags[] ToSerializedObjectTags(
        ImmutableDictionary<string, ImmutableArray<BaselineTag>> dict)
    {
        return dict.Select(kv => new SerializedObjectTags { Name = kv.Key, Tags = kv.Value.ToArray() })
            .ToArray();
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
        return dict.Select(kv => new SerializedEnumValues { Name = kv.Key, Values = kv.Value.ToArray() })
            .ToArray();
    }

    private static ImmutableDictionary<string, ImmutableArray<string>> FromSerializedArray(
        SerializedEnumValues[] arr)
    {
        return arr.ToImmutableDictionary(e => e.Name, e => e.Values.ToImmutableArray());
    }

    private static SerializedGroupMemberships[] ToSerializedGroupMemberships(
        ImmutableDictionary<string, ImmutableArray<GroupMembership>> dict)
    {
        return dict.Select(kv => new SerializedGroupMemberships
                { GroupKey = kv.Key, Members = kv.Value.ToArray() })
            .ToArray();
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