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
public sealed record GetLocalisationRowsResult(
    IReadOnlyList<LocRowDto> Rows,
    IReadOnlyList<string> Languages,
    string ContentHash,
    string Category = LocCategory.Text,
    bool Ordered = false,
    string? Error = null);
