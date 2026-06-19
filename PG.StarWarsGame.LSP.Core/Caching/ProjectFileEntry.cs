// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Caching;

[MessagePackObject]
public sealed class ProjectFileEntry
{
    [Key(0)] public string RelativePath { get; set; } = string.Empty;
    [Key(1)] public string ContentHash { get; set; } = string.Empty;
    [Key(2)] public SerializedDocument Document { get; set; } = new();
}