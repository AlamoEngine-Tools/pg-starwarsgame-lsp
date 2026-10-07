// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Lua;

public interface ILuaRenameProvider
{
    WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index);
    RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index);

    /// <summary>
    ///     Whether the position names something this server renames - an XML object, a Lua global,
    ///     an engine name - so its answer stands even when it is a refusal. Anything else (a local,
    ///     a field) is the Lua analyzer's to rename.
    /// </summary>
    bool Claims(string uri, int line, int character, GameIndex index)
    {
        return true;
    }
}