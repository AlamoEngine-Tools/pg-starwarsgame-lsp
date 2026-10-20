// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <summary>
///     The game did not answer in time. A <see cref="TimeoutException" />, so every handler that
///     already treats a timeout as a lost game still does, with a message the adapter can translate.
/// </summary>
public sealed class LuaDebugTimeoutException(CatalogueText text)
    : TimeoutException(ICatalogueTextError.English(text)), ICatalogueTextError
{
    public CatalogueText Text { get; } = text;
}