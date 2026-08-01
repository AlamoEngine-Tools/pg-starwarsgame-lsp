// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     One language's value on a row.
///     <para>
///         A list of pairs rather than a dictionary on purpose: OmniSharp camel-cases dictionary
///         keys on the wire, turning <c>"ENGLISH"</c> into <c>"eNGLISH"</c>, which the old webview
///         had to undo with <c>toUpperCase()</c> on every payload. A pair survives untouched.
///     </para>
/// </summary>
public sealed record LocValueDto(string Language, string Value);

/// <summary>
///     One row of a localisation file, addressed by its position rather than its key.
///     <para>
///         Position is the identity because credits files allow duplicate keys and their order is
///         significant - a key cannot name the row to edit. Text files are read the same way so
///         both kinds share one editing model.
///     </para>
/// </summary>
/// <param name="Index">0-based position in the file.</param>
/// <param name="Key">The row's key. Empty for a blank spacer row, which credits files rely on.</param>
/// <param name="Values">One entry per declared language, empty string where the file has no value.</param>
/// <param name="Source">
///     The row exactly as it appeared, so an untouched row can be written back byte-for-byte.
///     Never sent to the client - it exists for the save path.
/// </param>
/// <param name="Leading">
///     Comments and blank lines that preceded this row, carried so they travel with it when rows
///     move and survive when rows are rewritten.
/// </param>
public sealed record LocRowDto(
    int Index,
    string Key,
    IReadOnlyList<LocValueDto> Values,
    string Source = "",
    string Leading = "");

/// <summary>
///     A parsed localisation file: its rows plus everything needed to put the file back together
///     unchanged.
/// </summary>
/// <param name="Rows">Rows in file order.</param>
/// <param name="Languages">Languages the file declares, in declaration order.</param>
/// <param name="Preamble">Everything before the first row - a CSV header, or leading comments.</param>
/// <param name="LineEnding">The file's own line ending, so a save does not normalise it.</param>
public sealed record LocDocument(
    IReadOnlyList<LocRowDto> Rows,
    IReadOnlyList<string> Languages,
    string Preamble = "",
    string LineEnding = "\n");
