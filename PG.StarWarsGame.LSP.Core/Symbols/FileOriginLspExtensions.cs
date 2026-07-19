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
}