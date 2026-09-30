// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <param name="Symbols">All game-object and SFX-event definitions projected from the shipped engine.</param>
/// <param name="BuiltAt">UTC timestamp when the baseline was generated.</param>
/// <param name="SourceManifestHash">
///     SHA-256 of <c>StarWarsG.exe</c> + <c>PerceptionFunctionG.dll</c> at baseline build time.
///     Informational only - not validated at LSP runtime. Intended for future version-mismatch
///     tooling once a game-install-path config exists.
/// </param>
/// <param name="DynamicEnumValues">
///     All values for every <see cref="EnumKind.DynamicXml" /> enum (DamageType, ArmorType,
///     MovementClassType, etc.) extracted from the shipped game files.
/// </param>
/// <param name="HardcodedEnumValues">
///     Subset of <see cref="DynamicEnumValues" /> values that are hardcoded in the shipped engine
///     (those listed after the "ABOVE this point" boundary comment in <c>GameConstants.xml</c>).
/// </param>
/// <param name="FileTypeMap">Maps relative XML file paths to their allowed content types.</param>
public sealed record BaselineIndex(
    ImmutableDictionary<string, GameSymbol> Symbols,
    DateTimeOffset BuiltAt,
    string SourceManifestHash,
    ImmutableDictionary<string, ImmutableArray<string>> DynamicEnumValues,
    ImmutableDictionary<string, ImmutableArray<string>> HardcodedEnumValues,
    ImmutableDictionary<string, ImmutableArray<string>> FileTypeMap
)
{
    public static readonly BaselineIndex Empty = new(
        ImmutableDictionary<string, GameSymbol>.Empty,
        DateTimeOffset.MinValue,
        string.Empty,
        ImmutableDictionary<string, ImmutableArray<string>>.Empty,
        ImmutableDictionary<string, ImmutableArray<string>>.Empty,
        ImmutableDictionary<string, ImmutableArray<string>>.Empty);

    /// <summary>
    ///     Group memberships extracted from shipped-game data (e.g. <c>Overlap_Test</c> values on
    ///     <c>SFXEvent</c> objects). Keyed case-insensitively by <see cref="GroupMembership.GroupKey" />.
    ///     Merged with workspace group memberships via <c>GameIndex.AllGroupMemberships</c>.
    /// </summary>
    public ImmutableDictionary<string, ImmutableArray<GroupMembership>> GroupMemberships { get; init; } =
        ImmutableDictionary.Create<string, ImmutableArray<GroupMembership>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Normalised asset-file paths shipped with the game (lowercase, forward-slash, e.g.
    ///     <c>data/art/textures/foo.tga</c>). Built offline by the BaselineBuilder; used together
    ///     with the workspace asset glob to validate and complete <c>textureFile</c>, <c>modelFile</c>,
    ///     <c>audioFile</c> and <c>mapFile</c> references. Case-insensitive set.
    /// </summary>
    public ImmutableHashSet<string> AssetFiles { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Bone names exposed by each shipped <c>.alo</c> model, keyed by normalised model path
    ///     (lowercase, forward-slash, e.g. <c>data/art/models/unit.alo</c>). Built offline by the
    ///     BaselineBuilder from the engine's ALO model loader; used together with the workspace
    ///     bone scan to complete <c>boneName</c> references. Case-insensitive lookup.
    /// </summary>
    public ImmutableDictionary<string, ImmutableArray<string>> ModelBones { get; init; } =
        ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Texture names carried INSIDE each shipped <c>.alo</c>, keyed the same way as
    ///     <see cref="ModelBones" />.
    /// </summary>
    /// <remarks>
    ///     A model's own skins are written in the binary, not in the XML, so validating them used
    ///     to mean opening the model during validation. MEASURED 2026-09-30: that was the whole
    ///     cost of a workspace diagnostics sweep - one prop file spent 17.2s of its 17.5s there.
    ///     Captured offline instead, like <see cref="ModelBones" />, from the same scan.
    ///     <para>
    ///         An empty entry means "this model names no textures" and is a real answer; a MISSING
    ///         entry means the catalog has never seen the model, and the caller must parse it
    ///         rather than report its textures as absent.
    ///     </para>
    /// </remarks>
    public ImmutableDictionary<string, ImmutableArray<string>> ModelTextures { get; init; } =
        ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Full child-tag tree of each shipped GameObject, keyed case-insensitively by object id. Built
    ///     offline by the BaselineBuilder; consumed by the effective-object merge engine so that variants
    ///     (<c>Variant_Of_Existing_Type</c>) whose base is a shipped object can be resolved. Empty for
    ///     objects whose tags were not captured.
    /// </summary>
    public ImmutableDictionary<string, ImmutableArray<BaselineTag>> ObjectTags { get; init; } =
        ImmutableDictionary.Create<string, ImmutableArray<BaselineTag>>(StringComparer.OrdinalIgnoreCase);
}