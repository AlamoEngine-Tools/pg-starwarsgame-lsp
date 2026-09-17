// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PG.Commons;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests;

/// <summary>
///     The container the tests resolve from: PG.Commons' hashing (the real CRC-32, because the
///     byte vectors are captured traffic), null logging, the Core file helper over a mock file
///     system, plus the debugger's own registrations.
/// </summary>
internal static class TestServices
{
    /// <param name="configure">
    ///     Runs before the debugger's registrations, so a test can pre-register what the
    ///     debugger only <c>TryAdd</c>s - a fake <see cref="TimeProvider" /> for one - or replace
    ///     the file system with one holding its own files.
    /// </param>
    public static IServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new MockFileSystem());
        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        PetroglyphCommons.ContributeServices(services);
        configure?.Invoke(services);
        services.AddLuaDebugServices();
        return services.BuildServiceProvider();
    }

    public static T Get<T>() where T : notnull
    {
        return Build().GetRequiredService<T>();
    }
}