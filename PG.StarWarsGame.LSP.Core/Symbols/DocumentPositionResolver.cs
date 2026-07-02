// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     Shared "what symbol/reference is under the cursor" scan, used by both XML and Lua position
///     resolution. References win over group memberships, which win over definitions on the same line.
///     Callers supply the language-specific parts: which references are eligible
///     (<paramref name="referenceFilter" /> is a parameter of <see cref="FindAtPosition" />), and how
///     wide a definition's range should be, or whether it should be considered at all
///     (<c>symbolRangeLength</c>, which returns <see langword="null" /> to skip a symbol).
/// </summary>
public static class DocumentPositionResolver
{
    public static (string Id, LspRange Range)? FindAtPosition(
        DocumentIndex docIndex, int line, int character,
        Func<GameReference, bool> referenceFilter,
        Func<GameSymbol, int?> symbolRangeLength)
    {
        foreach (var r in docIndex.References)
            if (referenceFilter(r) && r.Line == line && character >= r.Column && character < r.Column + r.Length)
                return (r.TargetId, new LspRange(
                    new Position(line, r.Column),
                    new Position(line, r.Column + r.Length)));

        if (!docIndex.GroupMemberships.IsDefault)
            foreach (var gm in docIndex.GroupMemberships)
                if (gm.TagLine == line && character >= gm.TagColumn && character < gm.TagColumn + gm.TagLength)
                    return (gm.Membership.GroupKey, new LspRange(
                        new Position(line, gm.TagColumn),
                        new Position(line, gm.TagColumn + gm.TagLength)));

        foreach (var s in docIndex.Symbols)
            if (s.Origin is FileOrigin fo && fo.Line == line)
            {
                var len = symbolRangeLength(s);
                if (len is null) continue;

                var col = fo.Column ?? 0;
                return (s.Id, new LspRange(
                    new Position(line, col),
                    new Position(line, col + len.Value)));
            }

        return null;
    }
}
