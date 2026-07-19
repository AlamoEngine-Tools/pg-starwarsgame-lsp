// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Util;

public static class LuaPositionResolver
{
    public static (string Id, LspRange Range)? FindAtPosition(DocumentIndex docIndex, int line, int character)
    {
        return DocumentPositionResolver.FindAtPosition(
            docIndex, line, character,
            r => r.ExpectedKind == GameSymbolKind.LuaGlobal || r.ExpectedKind == GameSymbolKind.XmlObject,
            s => s.Kind == GameSymbolKind.LuaGlobal ? 0 : null);
    }
}