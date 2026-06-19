// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Caching;

[MessagePackObject]
public sealed class ProjectIndexSnapshot
{
    // Bumped whenever the DTO layout changes; a mismatched snapshot is treated as a full miss.
    public const int CurrentSchemaVersion = 1;

    [Key(0)] public int SchemaVersion { get; set; }

    [Key(1)] public string OverallHash { get; set; } = string.Empty;

    // Keyed by normalised pgproj path; value = that dependency's OverallHash at snapshot build time.
    [Key(2)] public SerializedDependencyHash[] DependencyHashes { get; set; } = [];
    [Key(3)] public ProjectFileEntry[] Files { get; set; } = [];
}