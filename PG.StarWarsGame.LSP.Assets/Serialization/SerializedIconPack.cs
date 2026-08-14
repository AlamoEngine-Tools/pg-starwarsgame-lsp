// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Assets.Serialization;

/// <summary>One baked icon: the mega texture directory name, and the PNG we cut for it.</summary>
/// <remarks>Public because MessagePack's dynamic resolver throws on internal types at runtime.</remarks>
[MessagePackObject]
public sealed class SerializedIcon
{
    [Key(0)] public string Name { get; set; } = string.Empty;
    [Key(1)] public byte[] Png { get; set; } = [];
}

/// <summary>
///     The base game's UI icons, pre-cut to PNGs at baseline-build time.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately a SIDECAR rather than a field on <see cref="SerializedBaseline" />. The pack
///         runs to roughly 3 MB for Forces of Corruption's 1107 icons, and most editing sessions
///         never open a preview - folding it into the main baseline would deserialize all of it on
///         every server start to serve a feature the user may never touch. Keeping it separate also
///         means the baseline's own schema version does not have to move for icon-only changes.
///     </para>
///     <para>
///         NOT gzipped, unlike the baseline. PNG is already deflate-compressed, so a second pass
///         costs build and load time for essentially nothing.
///     </para>
/// </remarks>
[MessagePackObject]
public sealed class SerializedIconPack
{
    /// <summary>v1: initial format.</summary>
    public const int CurrentSchemaVersion = 1;

    [Key(0)] public SerializedIcon[] Icons { get; set; } = [];
    [Key(1)] public int SchemaVersion { get; set; }

    /// <summary>
    ///     Hash of the game binaries this pack was cut from, matching the baseline built in the same
    ///     run. Lets a consumer notice a pack that has drifted from its baseline.
    /// </summary>
    [Key(2)] public string SourceManifestHash { get; set; } = string.Empty;

    [Key(3)] public long BuiltAtMs { get; set; }
}
