// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Core.Features;

/// <summary>
///     One language's hover. The server's hover handler asks every provider whether it
///     <see cref="Handles" /> the document and hands the request to the first that does, so a
///     language joins hover by registering a provider, never by editing the handler.
/// </summary>
public interface IHoverProvider
{
    /// <summary>Whether this provider answers for the document; <paramref name="documentUri" /> is normalized.</summary>
    bool Handles(string documentUri);

    Task<Hover?> HoverAsync(HoverParams request, CancellationToken ct);
}