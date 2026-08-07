// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

[Method("aet/getLocalisationRows", Direction.ClientToServer)]
public sealed record GetLocalisationRowsParams(string ProjectFilePath) : IRequest<GetLocalisationRowsResult>;

/// <summary>
///     A localisation file as the editor sees it: rows in file order, addressed by index.
/// </summary>
/// <param name="Rows">Rows in file order. Source and Leading are stripped - they serve the save path.</param>
/// <param name="Languages">Declared languages, in declaration order.</param>
/// <param name="ContentHash">
///     Echoed back on every write for this file (see <see cref="LocalisationConcurrencyGuard" />);
///     it is how the server detects the file changed on disk since the client last read it.
/// </param>
/// <param name="Category">
///     <see cref="LocCategory.Text" /> or <see cref="LocCategory.Credits" />.
/// </param>
/// <param name="Ordered">
///     Whether row order is significant and duplicate keys are legal. True for credits. The client
///     uses it to decide whether to offer reordering, and to stop treating the key as an identity.
/// </param>
/// <param name="CanAddLanguage">
///     Whether this file can gain another language <em>column</em>. False for the single-language
///     formats - <c>.properties</c> and <c>.dat</c>, which hold one language by construction and name
///     it in their file name. The rule belongs to <see cref="LocalisationDocumentEditor" />, which is
///     what refuses the command; it is reported here so the client does not stage an edit that will
///     be rejected, and so the list of single-language formats exists in one place.
/// </param>
/// <param name="AddLanguageCreatesFile">
///     Whether adding a language to this file means creating a sibling file rather than a column -
///     true for exactly the formats <paramref name="CanAddLanguage" /> is false for.
///     <para>
///         The two together tell the client which of the two routes to take, so "add a language"
///         stays one action for the user. It used to be greyed out here, which left no way at all to
///         add a language to a single-language project.
///     </para>
/// </param>
public sealed record GetLocalisationRowsResult(
    IReadOnlyList<LocRowDto> Rows,
    IReadOnlyList<string> Languages,
    string ContentHash,
    string Category = LocCategory.Text,
    bool Ordered = false,
    string? Error = null,
    bool CanAddLanguage = false,
    bool AddLanguageCreatesFile = false);
