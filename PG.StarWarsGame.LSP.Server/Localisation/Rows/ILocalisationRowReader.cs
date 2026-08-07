// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Parses a localisation file into the editor's row model.
///     <para>
///         Separate from the translation importers because those produce a key/value database,
///         which cannot represent a credits file (duplicate keys, significant order) and cannot say
///         where a row came from. The editor needs a document, not a lookup.
///     </para>
///     <para>
///         Text in, model out - no file access - so the caller decides what to read and this stays
///         testable without a filesystem.
///     </para>
/// </summary>
public interface ILocalisationRowReader
{
    /// <summary>
    ///     Parses <paramref name="text" /> according to <paramref name="extension" /> (with its
    ///     leading dot, lower-case).
    ///     <para>
    ///         <paramref name="fileName" /> is what a single-language format's language is read from -
    ///         a <c>.properties</c> file names its language nowhere else. Omit it and such a file falls
    ///         back to the workspace's configured game language; the multi-language formats ignore it.
    ///     </para>
    /// </summary>
    /// <exception cref="NotSupportedException">The extension has no row reader.</exception>
    LocDocument Read(string text, string extension, string? fileName = null);

    /// <summary>
    ///     Reads a file from disk.
    ///     <para>
    ///         Separate from <see cref="Read" /> because <c>.dat</c> is binary and cannot be handed
    ///         in as a string - the DAT services load from a path or a real file stream. Text
    ///         formats route straight back to <see cref="Read" />.
    ///     </para>
    /// </summary>
    LocDocument ReadFile(string filePath);
}
