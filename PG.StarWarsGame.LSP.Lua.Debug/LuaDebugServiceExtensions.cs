// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PG.StarWarsGame.LSP.Lua.Debug.Dap;
using PG.StarWarsGame.LSP.Lua.Debug.PgNet;
using PG.StarWarsGame.LSP.Lua.Debug.Protocol;
using PG.StarWarsGame.LSP.Lua.Debug.Session;
using PG.StarWarsGame.LSP.Lua.Debug.Sources;

namespace PG.StarWarsGame.LSP.Lua.Debug;

public static class LuaDebugServiceExtensions
{
    /// <summary>
    ///     Registers the Lua debugger's protocol, session and adapter services. The datagram codec
    ///     needs PG.Commons' <c>ICrc32HashingService</c>, which the host registers exactly once
    ///     through <c>PetroglyphCommons.ContributeServices</c>: the language server already gets
    ///     it from the localisation stack, the standalone adapter host does it in
    ///     <see cref="LuaDebugAdapterHost.CreateServices" />. Logging and <c>IFileHelper</c> are
    ///     the host's too.
    /// </summary>
    public static IServiceCollection AddLuaDebugServices(this IServiceCollection services)
    {
        // The reliable channel's resend timer and every timeout read this clock; a host or test
        // that registers its own TimeProvider first keeps it.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPgNetDatagramCodec, PgNetDatagramCodec>();
        services.AddSingleton<IConnectHandshake, ConnectHandshake>();
        services.AddSingleton<ILuaMessageCodec, LuaMessageCodec>();
        services.AddSingleton<IUdpTransportFactory, UdpTransportFactory>();
        services.AddSingleton<ICallstackParser, CallstackParser>();
        // Needs the host's IFileHelper, the same one the index resolves documents with.
        services.AddSingleton<IScriptSourceMapFactory, ScriptSourceMapFactory>();
        services.AddSingleton<IFrameLocalsProvider, FrameLocalsProvider>();
        services.AddSingleton<ILuaDebugSessionFactory, LuaDebugSessionFactory>();
        services.TryAddSingleton<IGameLauncher, ProcessGameLauncher>();
        // One channel per connection and one connection per session: each resolution is fresh.
        services.AddTransient<IReliableChannel, ReliableChannel>();
        services.AddTransient<ILuaDebugConnection, LuaDebugConnection>();
        services.AddTransient<ILuaDebugSession, LuaDebugSession>();
        services.AddTransient<LuaDebugAdapter>();
        return services;
    }
}