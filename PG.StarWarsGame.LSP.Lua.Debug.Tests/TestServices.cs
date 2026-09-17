// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using PG.Commons;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests;

/// <summary>
///     The container the tests resolve from: PG.Commons' hashing (the real CRC-32, because the
///     byte vectors are captured traffic) plus the debugger's own registrations. The hashing
///     service asks for a file system it never touches here, so it gets a mock.
/// </summary>
internal static class TestServices
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new MockFileSystem());
        PetroglyphCommons.ContributeServices(services);
        services.AddLuaDebugServices();
        return services.BuildServiceProvider();
    }

    public static T Get<T>() where T : notnull
    {
        return Build().GetRequiredService<T>();
    }
}