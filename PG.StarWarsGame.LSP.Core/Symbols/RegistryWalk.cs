// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     One file registry as the game loads it: the copy of the registry it reads and every entry of
///     that copy in list order, a repeat kept. The order decides which of two definitions of one
///     name the registry's <see cref="Schema.DuplicateNameRule" /> keeps.
/// </summary>
/// <param name="RegistryPath">The registry's game path in key form, <c>data/xml/gameobjectfiles.xml</c>.</param>
/// <param name="RegistryUri">The layer copy of the registry the game reads, or null for the shipped one.</param>
/// <param name="Entries">The registry's entries in list order.</param>
public sealed record RegistryWalk(string RegistryPath, string? RegistryUri, ImmutableArray<RegistryWalkEntry> Entries);

/// <param name="Key">The entry's XML-directory-relative path in key form, <c>units/foo.xml</c>.</param>
/// <param name="LayerUri">
///     The project-layer copy the game opens, highest layer first; null when no layer ships the file,
///     so the game opens the shipped copy if there is one and skips the entry if not.
/// </param>
public sealed record RegistryWalkEntry(string Key, string? LayerUri);