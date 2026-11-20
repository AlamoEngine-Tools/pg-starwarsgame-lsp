// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     One part of a model as its file declares it: a mesh or a proxy, by name, with the two facts a
///     rule about what stays drawn needs (#449).
/// </summary>
/// <remarks>
///     <para>
///         Two booleans per part and nothing else - never the shader, the material or any geometry,
///         so the baseline carrying these redistributes nothing of the game's files.
///     </para>
///     <para>
///         MessagePack types must be public - an internal one throws at runtime, not at compile time.
///     </para>
/// </remarks>
/// <param name="Name">The mesh's or the proxy's name, as the file spells it.</param>
/// <param name="Proxy">A proxy - an effect the model carries - rather than a mesh.</param>
/// <param name="FileVisible">
///     Whether the file marks the part visible. Both formats store a hidden flag; this is its inverse.
/// </param>
/// <param name="Surface">
///     Whether the mesh is drawn as a surface at all: false for a shadow volume and the other meshes
///     whose material the game uses but never shows. Always false for a proxy, which is no mesh.
/// </param>
[MessagePackObject]
public sealed record ModelPart(
    [property: Key(0)] string Name,
    [property: Key(1)] bool Proxy,
    [property: Key(2)] bool FileVisible,
    [property: Key(3)] bool Surface);