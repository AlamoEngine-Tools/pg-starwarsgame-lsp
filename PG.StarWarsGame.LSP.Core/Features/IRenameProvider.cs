// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Core.Features;

/// <summary>
///     One language's rename and prepare-rename. The server's rename handlers ask every provider
///     whether it <see cref="Handles" /> the document and hand the request to the first that does,
///     so a language joins rename by registering a provider, never by editing the handlers.
/// </summary>
public interface IRenameProvider
{
    /// <summary>Whether this provider answers for the document; <paramref name="documentUri" /> is normalized.</summary>
    bool Handles(string documentUri);

    Task<WorkspaceEdit?> RenameAsync(RenameParams request, CancellationToken ct);

    Task<RangeOrPlaceholderRange?> PrepareAsync(PrepareRenameParams request, CancellationToken ct);
}