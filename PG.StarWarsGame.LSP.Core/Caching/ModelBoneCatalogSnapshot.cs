// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Caching;

/// <summary>
///     One layer's extracted model-bone catalog, persisted so a restart does not re-parse every
///     <c>.alo</c> under that layer's asset roots.
/// </summary>
/// <remarks>
///     <para>
///         Stored per LAYER rather than per workspace, exactly like
///         <see cref="ProjectIndexSnapshot" />: several mod leaves commonly share one dependency,
///         and each keeping its own copy of that dependency's extraction is the cost this removes
///         twice over.
///     </para>
///     <para>
///         MessagePack types must be public - an internal one throws at runtime, not at compile
///         time.
///     </para>
/// </remarks>
[MessagePackObject]
public sealed class ModelBoneCatalogSnapshot
{
    /// <summary>
    ///     Bumped whenever what the extractor EMITS changes - a new ALO reader, a different notion
    ///     of which names count as bones, a change to <see cref="Symbols.ModelBoneKey" />.
    /// </summary>
    /// <remarks>
    ///     <see cref="Fingerprint" /> covers the model FILES, never the code that reads them, so
    ///     without this a snapshot written by an older extractor replays its output forever - the
    ///     files are unchanged, so the fingerprint keeps matching and the catalog is never rebuilt.
    ///     History: 1 = initial (2026-09-30); 2 = entries carry Textures as well as Bones
    ///     (2026-09-30) - a v1 snapshot has none, and an absent texture list means "never scanned",
    ///     which would send the validator back to parsing every model on demand.
    /// </remarks>
    public const int CurrentSchemaVersion = 2;

    [Key(0)] public int SchemaVersion { get; set; }

    /// <summary>
    ///     <see cref="ModelBoneFingerprint" /> of the asset roots this was extracted from. A
    ///     snapshot whose fingerprint does not match the tree on disk is discarded unread.
    /// </summary>
    [Key(1)]
    public string Fingerprint { get; set; } = string.Empty;

    [Key(2)] public SerializedModelBones[] Models { get; set; } = [];
}

/// <summary>One model's bone names, keyed the way every other consumer spells a model.</summary>
[MessagePackObject]
public sealed class SerializedModelBones
{
    /// <summary>The bare lowercased filename - see <see cref="Symbols.ModelBoneKey" />.</summary>
    [Key(0)]
    public string ModelKey { get; set; } = string.Empty;

    [Key(1)] public string[] Bones { get; set; } = [];

    /// <summary>
    ///     The texture names this model carries inside itself. Empty is a REAL answer ("names no
    ///     textures"); the model being absent from the snapshot entirely is what means "unscanned".
    /// </summary>
    [Key(2)]
    public string[] Textures { get; set; } = [];
}