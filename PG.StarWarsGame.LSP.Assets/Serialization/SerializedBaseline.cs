// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Serialization;

[MessagePackObject]
public sealed class SerializedBaseline
{
    // Bumped whenever the DTO layout OR the content contract changes (e.g. new required
    // FileTypeMap entries); a mismatched (or absent - see Deserialize) baseline is discarded
    // cleanly instead of being interpreted against the wrong layout. Key(10) is appended after
    // the original 10 fields rather than inserted at Key(0), so pre-versioning baselines (which
    // never wrote this key) deserialize it as the CLR default 0 - itself a natural version
    // mismatch, with no separate migration path needed.
    // v2: FileTypeMap now carries the story chain (StoryPlotManifest/StoryParser entries).
    public const int CurrentSchemaVersion = 2;

    [Key(0)] public GameSymbol[] Symbols { get; set; } = [];
    [Key(1)] public long BuiltAtMs { get; set; }
    [Key(2)] public string SourceManifestHash { get; set; } = string.Empty;
    [Key(3)] public SerializedEnumValues[] DynamicEnumValues { get; set; } = [];
    [Key(4)] public SerializedEnumValues[] HardcodedEnumValues { get; set; } = [];
    [Key(5)] public SerializedEnumValues[] FileTypeMap { get; set; } = [];
    [Key(6)] public SerializedGroupMemberships[] GroupMemberships { get; set; } = [];
    [Key(7)] public string[] AssetFiles { get; set; } = [];
    [Key(8)] public SerializedEnumValues[] ModelBones { get; set; } = [];
    [Key(9)] public SerializedObjectTags[] ObjectTags { get; set; } = [];
    [Key(10)] public int SchemaVersion { get; set; }
}