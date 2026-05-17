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
            Symbols             = baseline.Symbols.Values.ToArray(),
            BuiltAtMs           = baseline.BuiltAt.ToUnixTimeMilliseconds(),
            SourceManifestHash  = baseline.SourceManifestHash,
            DynamicEnumValues   = ToSerializedArray(baseline.DynamicEnumValues),
            HardcodedEnumValues = ToSerializedArray(baseline.HardcodedEnumValues)
        };
        var msgpack = MessagePackSerializer.Serialize(dto);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal))
            gz.Write(msgpack);
        return ms.ToArray();
    }

    public static BaselineIndex Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        gz.CopyTo(decompressed);
        var dto      = MessagePackSerializer.Deserialize<SerializedBaseline>(decompressed.ToArray());
        var builtAt  = DateTimeOffset.FromUnixTimeMilliseconds(dto.BuiltAtMs);
        var symbols  = dto.Symbols.ToImmutableDictionary(s => s.Id);
        var enums    = FromSerializedArray(dto.DynamicEnumValues);
        var hardcoded = FromSerializedArray(dto.HardcodedEnumValues);
        return new BaselineIndex(symbols, builtAt, dto.SourceManifestHash, enums, hardcoded);
    }

    private static SerializedEnumValues[] ToSerializedArray(
        ImmutableDictionary<string, ImmutableArray<string>> dict) =>
        dict.Select(kv => new SerializedEnumValues { Name = kv.Key, Values = kv.Value.ToArray() })
            .ToArray();

    private static ImmutableDictionary<string, ImmutableArray<string>> FromSerializedArray(
        SerializedEnumValues[] arr) =>
        arr.ToImmutableDictionary(e => e.Name, e => e.Values.ToImmutableArray());
}

[MessagePackObject]
public sealed class SerializedBaseline
{
    [Key(0)] public GameSymbol[]           Symbols             { get; set; } = [];
    [Key(1)] public long                   BuiltAtMs           { get; set; }
    [Key(2)] public string                 SourceManifestHash  { get; set; } = string.Empty;
    [Key(3)] public SerializedEnumValues[] DynamicEnumValues   { get; set; } = [];
    [Key(4)] public SerializedEnumValues[] HardcodedEnumValues { get; set; } = [];
}

[MessagePackObject]
public sealed class SerializedEnumValues
{
    [Key(0)] public string   Name   { get; set; } = string.Empty;
    [Key(1)] public string[] Values { get; set; } = [];
}
