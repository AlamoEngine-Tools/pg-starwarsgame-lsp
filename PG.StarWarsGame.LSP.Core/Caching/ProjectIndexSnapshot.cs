// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Caching;

[MessagePackObject]
public sealed class ProjectIndexSnapshot
{
    // Bumped whenever the DTO layout changes OR the parser's output semantics change (new
    // reference kinds, id scoping, group membership shapes, …); a mismatched snapshot is treated
    // as a full miss. Content hashes cannot catch semantic staleness: a snapshot written by an
    // older parser replays its pre-fix output forever, file by file, until each file happens to
    // be edited — so any change to what XmlGameDocumentParser/LuaGameDocumentParser emit MUST
    // come with a bump here.
    // History: 1 = initial; 2 = discard pre-2026-07 snapshots lacking enum:-prefixed references
    // and owner-scoped ability ids (parser changes of 2026-07-02/04 shipped without a bump).
    public const int CurrentSchemaVersion = 2;

    [Key(0)] public int SchemaVersion { get; set; }

    [Key(1)] public string OverallHash { get; set; } = string.Empty;

    // Keyed by normalised pgproj path; value = that dependency's OverallHash at snapshot build time.
    [Key(2)] public SerializedDependencyHash[] DependencyHashes { get; set; } = [];
    [Key(3)] public ProjectFileEntry[] Files { get; set; } = [];
}