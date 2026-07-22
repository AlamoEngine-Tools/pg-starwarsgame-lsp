// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public static class FileOriginLspExtensions
{
    /// <summary>Builds a zero-width LSP <see cref="Location" /> at this origin's line/column.</summary>
    public static Location ToLspLocation(this FileOrigin origin)
    {
        var col = origin.Column ?? 0;
        return new Location
        {
            Uri = origin.Uri,
            Range = new LspRange(new Position(origin.Line, col), new Position(origin.Line, col))
        };
    }

    /// <summary>
    ///     Builds an LSP <see cref="LocationLink" /> pointing at this origin, tagging the source span
    ///     (<paramref name="originSelectionRange" />) that the client should treat as the clickable link.
    ///     Supplying it makes the Ctrl-hover underline/pointer deterministic instead of leaving the client
    ///     to guess the span from its word pattern.
    /// </summary>
    public static LocationLink ToLspLocationLink(this FileOrigin origin, LspRange originSelectionRange)
    {
        var location = origin.ToLspLocation();
        return new LocationLink
        {
            TargetUri = location.Uri,
            TargetRange = location.Range,
            TargetSelectionRange = location.Range,
            OriginSelectionRange = originSelectionRange
        };
    }
}