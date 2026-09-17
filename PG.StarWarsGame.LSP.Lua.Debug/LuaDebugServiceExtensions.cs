// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Lua.Debug.PgNet;

namespace PG.StarWarsGame.LSP.Lua.Debug;

public static class LuaDebugServiceExtensions
{
    /// <summary>
    ///     Registers the Lua debugger's protocol services. The datagram codec needs PG.Commons'
    ///     <c>ICrc32HashingService</c>, which the host registers exactly once through
    ///     <c>PetroglyphCommons.ContributeServices</c>: the language server already gets it from
    ///     the localisation stack, a standalone debug-adapter host has to call it itself.
    /// </summary>
    public static IServiceCollection AddLuaDebugServices(this IServiceCollection services)
    {
        services.AddSingleton<IPgNetDatagramCodec, PgNetDatagramCodec>();
        services.AddSingleton<IConnectHandshake, ConnectHandshake>();
        return services;
    }
}
