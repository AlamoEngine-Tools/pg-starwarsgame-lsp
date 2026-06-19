// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Caching;

[MessagePackObject]
public sealed class SerializedDocument
{
    [Key(0)] public GameSymbol[] Symbols { get; set; } = [];
    [Key(1)] public SerializedReference[] References { get; set; } = [];
    [Key(2)] public string[] RequireArgs { get; set; } = [];
    [Key(3)] public SerializedDocumentGroupMembership[] GroupMemberships { get; set; } = [];
    [Key(4)] public int LayerRank { get; set; }
    [Key(5)] public string? LayerName { get; set; }
}