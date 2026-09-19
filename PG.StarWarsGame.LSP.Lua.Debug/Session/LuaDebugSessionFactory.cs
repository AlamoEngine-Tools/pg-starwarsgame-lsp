// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <inheritdoc />
public sealed class LuaDebugSessionFactory : ILuaDebugSessionFactory
{
    private readonly IServiceProvider _services;

    public LuaDebugSessionFactory(IServiceProvider services)
    {
        _services = services;
    }

    public ILuaDebugSession Create()
    {
        return _services.GetRequiredService<ILuaDebugSession>();
    }
}