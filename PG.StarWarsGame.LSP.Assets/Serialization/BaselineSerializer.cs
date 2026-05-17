using System.Collections.Immutable;
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
            SourceManifestHash  = baseline.SourceManifestHash
        };
        return MessagePackSerializer.Serialize(dto);
    }

    public static BaselineIndex Deserialize(byte[] data)
    {
        var dto     = MessagePackSerializer.Deserialize<SerializedBaseline>(data);
        var builtAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.BuiltAtMs);
        var symbols = dto.Symbols.ToImmutableDictionary(s => s.Id);
        return new BaselineIndex(symbols, builtAt, dto.SourceManifestHash);
    }
}

[MessagePackObject]
public sealed class SerializedBaseline
{
    [Key(0)] public GameSymbol[] Symbols            { get; set; } = [];
    [Key(1)] public long         BuiltAtMs           { get; set; }
    [Key(2)] public string       SourceManifestHash  { get; set; } = string.Empty;
}
