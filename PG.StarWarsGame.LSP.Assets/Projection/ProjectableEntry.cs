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
///     Null/empty when tags were not captured — the object then contributes no <c>ObjectTags</c> entry.
/// </param>
public readonly record struct ProjectableEntry(
    string Name,
    string ClassificationName,
    XmlLocationInfo Location,
    IReadOnlyList<BaselineTag>? Tags = null
);