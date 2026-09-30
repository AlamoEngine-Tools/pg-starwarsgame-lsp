// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PG.StarWarsGame.LSP.Lua.Debug.PgNet;
using PG.StarWarsGame.LSP.Lua.Debug.Protocol;
using PG.StarWarsGame.LSP.Lua.Debug.Session;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests;

public sealed class LuaDebugServiceExtensionsTest
{
    [Fact]
    public void AddLuaDebugServices_WithCommonsHashing_ResolvesTheSingletons()
    {
        var provider = TestServices.Build();

        Assert.IsType<PgNetDatagramCodec>(provider.GetRequiredService<IPgNetDatagramCodec>());
        Assert.IsType<ConnectHandshake>(provider.GetRequiredService<IConnectHandshake>());
        Assert.IsType<LuaMessageCodec>(provider.GetRequiredService<ILuaMessageCodec>());
        Assert.IsType<UdpTransportFactory>(provider.GetRequiredService<IUdpTransportFactory>());
        Assert.Same(provider.GetRequiredService<ILuaMessageCodec>(), provider.GetRequiredService<ILuaMessageCodec>());
    }

    [Fact]
    public void AddLuaDebugServices_ChannelConnectionAndSession_AreOnePerResolution()
    {
        var provider = TestServices.Build();

        Assert.NotSame(provider.GetRequiredService<IReliableChannel>(),
            provider.GetRequiredService<IReliableChannel>());
        Assert.NotSame(provider.GetRequiredService<ILuaDebugConnection>(),
            provider.GetRequiredService<ILuaDebugConnection>());
        Assert.NotSame(provider.GetRequiredService<ILuaDebugSession>(),
            provider.GetRequiredService<ILuaDebugSession>());
        Assert.IsType<LuaDebugSession>(provider.GetRequiredService<ILuaDebugSession>());
    }

    [Fact]
    public void AddLuaDebugServices_NoTimeProviderRegistered_UsesTheSystemClock()
    {
        var provider = TestServices.Build();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AddLuaDebugServices_TimeProviderRegisteredFirst_IsKept()
    {
        var fake = new FakeTimeProvider();

        var provider = TestServices.Build(services => services.AddSingleton<TimeProvider>(fake));

        Assert.Same(fake, provider.GetRequiredService<TimeProvider>());
    }

    // This used to assert the opposite: that the codec could NOT be resolved without the host's
    // PG.Commons contribution. That contract is gone. As of the 4.1.4 packages a consumer cannot
    // obtain an ICrc32HashingService at all - PetroglyphCommons.ContributeServices is empty and
    // both the service and its CRC32 provider are internal - so the codec computes the checksum
    // from System.IO.Hashing.Crc32, which is what PG.Commons wrapped anyway.
    [Fact]
    public void AddLuaDebugServices_NeedsNoPetroglyphHashing_ToResolveTheCodec()
    {
        var provider = new ServiceCollection().AddLuaDebugServices().BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IPgNetDatagramCodec>());
    }
}