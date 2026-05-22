// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Assets.Serialization;

[MessagePackObject]
public sealed class SerializedEnumValues
{
    [Key(0)] public string Name { get; set; } = string.Empty;
    [Key(1)] public string[] Values { get; set; } = [];
}