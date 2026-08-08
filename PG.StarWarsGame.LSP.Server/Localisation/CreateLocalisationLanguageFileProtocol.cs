// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Localisation;

/// <summary>
///     Adds a language to a single-language localisation project by creating a sibling file for it.
/// </summary>
/// <remarks>
///     The multi-language formats add a language as a column, which is a text edit to the file being
///     edited and therefore travels as an <c>addLanguage</c> command in the normal batch. A
///     single-language format cannot take a column - another language means another file - so it
///     cannot go through the batch at all: the batch composes new text for one existing file, and
///     this creates a different one.
///     <para>
///         Before this existed the editor simply greyed the action out, and nothing anywhere in the
///         extension could create a localisation file.
///     </para>
/// </remarks>
/// <param name="ProjectFilePath">An existing file in the set; the new file is created beside it.</param>
/// <param name="Language">An Alamo language identifier, e.g. <c>GERMAN</c>.</param>
[Method("aet/createLocalisationLanguageFile", Direction.ClientToServer)]
public sealed record CreateLocalisationLanguageFileParams(string ProjectFilePath, string Language)
    : IRequest<CreateLocalisationLanguageFileResult>;

/// <param name="WrittenPath">The new file, or null if nothing was written.</param>
public sealed record CreateLocalisationLanguageFileResult(
    string? WrittenPath,
    string? Error = null);
