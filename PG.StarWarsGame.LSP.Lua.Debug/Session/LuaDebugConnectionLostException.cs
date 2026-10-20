// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <summary>
///     The game is gone: it said goodbye, reset the socket, or fell silent for longer than the
///     heartbeat allows. Every request in flight fails with this.
/// </summary>
public sealed class LuaDebugConnectionLostException : Exception, ICatalogueTextError
{
    public LuaDebugConnectionLostException(CatalogueText text) : base(ICatalogueTextError.English(text))
    {
        Text = text;
    }

    public LuaDebugConnectionLostException(CatalogueText text, Exception innerException)
        : base(ICatalogueTextError.English(text), innerException)
    {
        Text = text;
    }

    public CatalogueText Text { get; }
}