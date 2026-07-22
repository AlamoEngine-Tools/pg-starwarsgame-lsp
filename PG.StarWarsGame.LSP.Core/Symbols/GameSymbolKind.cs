// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

public enum GameSymbolKind
{
    XmlObject,
    Asset,
    LuaGlobal,
    LocalisationKey,

    /// <summary>
    ///     A workspace file (story plot manifest, story thread, or Lua script) indexed as a
    ///     navigable symbol keyed via <see cref="WorkspaceFileKey" />, so a
    ///     <see cref="Schema.ReferenceKind.WorkspaceFile" /> reference resolves to the file.
    /// </summary>
    WorkspaceFile
}