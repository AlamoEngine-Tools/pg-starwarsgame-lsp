// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;

namespace PG.StarWarsGame.LSP.Localisation.Rows;

/// <summary>
///     A problem with a translation entry.
/// </summary>
/// <param name="Key">
///     The entry the problem sits on, or null when it is about the batch as a whole. A key rather
///     than a row number because a translation file has no row order the user can see.
/// </param>
/// <param name="Severity"><c>error</c> or <c>warning</c>.</param>
/// <param name="Index">
///     The row the problem sits on, so the grid can highlight it. Needed as well as
///     <paramref name="Key" /> because the one problem a key cannot identify is a row that has no
///     key - and a blank key is precisely the row worth pointing at.
/// </param>
[PublicAPI]
public sealed record LocTranslationProblemDto(
    string? Key,
    string? Language,
    string Severity,
    string Message,
    int? Index = null);