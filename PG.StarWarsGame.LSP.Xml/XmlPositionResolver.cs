// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

public static class XmlPositionResolver
{
    public static (string Id, LspRange Range)? FindAtPosition(DocumentIndex docIndex, int line, int character)
    {
        foreach (var r in docIndex.References)
            if (r.Line == line && character >= r.Column && character < r.Column + r.Length)
                return (r.TargetId, new LspRange(
                    new Position(line, r.Column),
                    new Position(line, r.Column + r.Length)));

        foreach (var s in docIndex.Symbols)
            if (s.Origin is FileOrigin fo && fo.Line == line)
            {
                var col = fo.Column ?? 0;
                return (s.Id, new LspRange(
                    new Position(line, col),
                    new Position(line, col + s.Id.Length)));
            }

        return null;
    }
}