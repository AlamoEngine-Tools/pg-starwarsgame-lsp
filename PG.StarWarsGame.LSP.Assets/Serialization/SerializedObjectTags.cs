// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Serialization;

[MessagePackObject]
public sealed class SerializedObjectTags
{
    [Key(0)] public string Name { get; set; } = string.Empty;
    [Key(1)] public BaselineTag[] Tags { get; set; } = [];
}