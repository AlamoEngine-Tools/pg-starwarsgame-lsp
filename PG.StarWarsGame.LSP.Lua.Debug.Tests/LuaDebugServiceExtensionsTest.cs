// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Lua.Debug.PgNet;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests;

public sealed class LuaDebugServiceExtensionsTest
{
    [Fact]
    public void AddLuaDebugServices_WithCommonsHashing_ResolvesCodecAndHandshakeAsSingletons()
    {
        var provider = TestServices.Build();

        var codec = provider.GetRequiredService<IPgNetDatagramCodec>();
        var handshake = provider.GetRequiredService<IConnectHandshake>();

        Assert.IsType<PgNetDatagramCodec>(codec);
        Assert.IsType<ConnectHandshake>(handshake);
        Assert.Same(codec, provider.GetRequiredService<IPgNetDatagramCodec>());
        Assert.Same(handshake, provider.GetRequiredService<IConnectHandshake>());
    }

    [Fact]
    public void AddLuaDebugServices_WithoutCommonsHashing_CodecCannotBeResolved()
    {
        // The host owns the one-time PG.Commons contribution; the debugger never registers it
        // itself, because a second CRC-32 provider makes the hashing service refuse to start.
        var provider = new ServiceCollection().AddLuaDebugServices().BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IPgNetDatagramCodec>());
    }
}
