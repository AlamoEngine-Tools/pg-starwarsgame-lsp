// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Caching;

[MessagePackObject]
public sealed class SerializedDependencyHash
{
    [Key(0)] public string ProjectPath { get; set; } = string.Empty;
    [Key(1)] public string OverallHash { get; set; } = string.Empty;
}