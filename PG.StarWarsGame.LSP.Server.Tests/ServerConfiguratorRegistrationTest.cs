// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using AnakinRaW.CommonUtilities.Hashing;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.Files.MTD.Services;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>
///     Guards the composition root itself.
/// </summary>
/// <remarks>
///     <para>
///         Every other test in this project builds handlers and services by hand, so nothing
///         exercised <see cref="ServerConfigurator" />'s registrations. A duplicate registration
///         once shipped with all 893 tests green while the server crashed on every launch with
///         "Hash provider with key 'CRC32' is already registered" - the whole suite passed because
///         no test ever built the real container.
///     </para>
///     <para>
///         This resolves the services whose registrations are easy to get wrong: the ones several
///         Petroglyph <c>SupportXxx</c> helpers each try to contribute. It cannot replace actually
///         launching the server, since the full graph needs LSP-supplied services that only exist at
///         runtime, but it does turn the specific class of mistake that caused that crash into a
///         test failure.
///     </para>
/// </remarks>
public sealed class ServerConfiguratorRegistrationTest
{
    private static ServiceProvider BuildProvider()
    {
        var options = new LanguageServerOptions();
        ServerConfigurator.Apply(options);
        return options.Services.BuildServiceProvider();
    }

    /// <summary>
    ///     <c>HashingService</c>'s constructor walks every registered
    ///     <c>IHashAlgorithmProvider</c> and throws when two claim the same key, so resolving it at
    ///     all is the assertion. This is the exact failure that took the server down.
    /// </summary>
    [Fact]
    public void HashingService_ResolvesWithoutDuplicateProviders()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IHashingService>());
    }

    /// <summary>The MTD reader is what the icon pipeline is built on; it depends on that hashing.</summary>
    [Fact]
    public void MtdFileService_Resolves()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IMtdFileService>());
    }

    // A provider registered twice would hand HashingService two claimants for the same key.
    [Fact]
    public void HashAlgorithmProviders_AreRegisteredExactlyOncePerKey()
    {
        using var provider = BuildProvider();

        var providers = provider.GetServices<IHashAlgorithmProvider>().ToList();

        Assert.Equal(providers.Select(p => p.SupportedHashType).Distinct().Count(), providers.Count);
    }
}
