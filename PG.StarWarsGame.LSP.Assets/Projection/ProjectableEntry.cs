// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.Files.XML;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     A named game object ready for projection into a <see cref="Core.Symbols.GameSymbol" />.
///     Wraps a <see cref="PG.StarWarsGame.Files.XML.Data.NamedXmlObject" /> without depending on
///     the concrete engine type, keeping the projector testable.
/// </summary>
/// <param name="Tags">
///     The object's child tags, captured for the baseline tag tree (variant inheritance support).
///     Null/empty when tags were not captured - the object then contributes no <c>ObjectTags</c> entry.
/// </param>
/// <param name="Behaviors">
///     The behaviour tokens this object declares, read from the game XML by
///     <see cref="GameObjectFactsReader" />. Separate from <paramref name="Tags" /> because the
///     engine's object model carries no behaviours at all, so they are fetched on their own rather
///     than falling out of a tag tree that is not captured for game objects. Null when the object
///     declares none; the projector then falls back to <paramref name="Tags" />, which is what
///     supplies them for anything whose tags ARE captured.
/// </param>
/// <param name="Flags">
///     The tracked boolean tags that are true on this object. Empty means inspected and none set;
///     null means nobody looked, which a kind resting on a flag must not read as "no".
/// </param>
public readonly record struct ProjectableEntry(
    string Name,
    string ClassificationName,
    XmlLocationInfo Location,
    IReadOnlyList<BaselineTag>? Tags = null,
    string[]? Behaviors = null,
    string[]? Flags = null
);