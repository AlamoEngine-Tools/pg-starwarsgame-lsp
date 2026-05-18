// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Serialization;

[MessagePackObject]
public sealed class SerializedBaseline
{
    [Key(0)] public GameSymbol[] Symbols { get; set; } = [];
    [Key(1)] public long BuiltAtMs { get; set; }
    [Key(2)] public string SourceManifestHash { get; set; } = string.Empty;
    [Key(3)] public SerializedEnumValues[] DynamicEnumValues { get; set; } = [];
    [Key(4)] public SerializedEnumValues[] HardcodedEnumValues { get; set; } = [];
}
