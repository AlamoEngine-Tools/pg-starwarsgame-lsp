// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml;

public interface IXmlRenameProvider
{
    WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index);
    RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index);
}