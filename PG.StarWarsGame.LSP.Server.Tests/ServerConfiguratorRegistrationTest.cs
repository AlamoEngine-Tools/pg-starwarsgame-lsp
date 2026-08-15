// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using AnakinRaW.CommonUtilities.Hashing;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.Files.MEG.Services;
using PG.StarWarsGame.Files.MTD.Services;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

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
    ///     The container with a placeholder for the one service the LSP host supplies at runtime.
    /// </summary>
    /// <remarks>
    ///     Several services reach <c>WindowUserNotifier</c>, which needs an
    ///     <c>ILanguageServerFacade</c> that does not exist outside a running server. Standing one in
    ///     costs nothing here - nothing under test calls it - and it extends this guard from the few
    ///     leaf services that happen to have no such dependency to the ones that do, which is most of
    ///     them.
    /// </remarks>
    private static ServiceProvider BuildProviderWithHostStubs()
    {
        var options = new LanguageServerOptions();
        ServerConfigurator.Apply(options);
        options.Services.AddSingleton<ILanguageServerFacade>(_ => null!);
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

    /// <summary>
    ///     The MEG reader behind the model preview's archive tier.
    /// </summary>
    /// <remarks>
    ///     <c>SupportMEG</c> was added next to <c>SupportMTD</c> and carries the same hazard: if it
    ///     ever begins contributing PG.Commons' hashing itself, the CRC32 duplicate returns. Resolving
    ///     it here is what keeps that a test failure rather than a crash on every launch.
    /// </remarks>
    [Fact]
    public void MegFileService_Resolves()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IMegFileService>());
    }

    /// <summary>
    ///     The preview's asset resolution, resolved from the real container rather than constructed by
    ///     hand - the only way a missing or mis-ordered registration in its dependency chain shows up.
    /// </summary>
    [Fact]
    public void GameAssetResolver_Resolves()
    {
        using var provider = BuildProviderWithHostStubs();

        Assert.NotNull(provider.GetRequiredService<IGameAssetResolver>());
        Assert.NotNull(provider.GetRequiredService<IMegArchiveSet>());
    }

    /// <summary>
    ///     The scene builder, which sits on top of the asset resolver and the game index.
    /// </summary>
    [Fact]
    public void PreviewSceneBuilder_Resolves()
    {
        using var provider = BuildProviderWithHostStubs();

        Assert.NotNull(provider.GetRequiredService<PreviewSceneBuilder>());
    }

    /// <summary>
    ///     The preview endpoints, resolved as the LSP host would construct them.
    /// </summary>
    /// <remarks>
    ///     Handlers registered through <c>WithHandler</c> go into the LSP host's own container, not
    ///     this one, so they cannot be resolved directly. <c>ActivatorUtilities</c> constructs them the
    ///     same way the host does - satisfying every constructor parameter from the container - which
    ///     is the part worth checking: a handler asking for something unregistered would otherwise
    ///     surface only the first time a client sent the request.
    /// </remarks>
    [Fact]
    public void PreviewHandlers_Resolve()
    {
        using var provider = BuildProviderWithHostStubs();

        Assert.NotNull(ActivatorUtilities.CreateInstance<GetPreviewSceneHandler>(provider));
        Assert.NotNull(ActivatorUtilities.CreateInstance<GetModelGlbHandler>(provider));
        Assert.NotNull(ActivatorUtilities.CreateInstance<GetModelTextureHandler>(provider));
    }

    /// <summary>
    ///     Indexing archives must not happen just because the service was constructed.
    /// </summary>
    /// <remarks>
    ///     The archive set is lazy so that a workspace with no game directory configured - which is
    ///     every workspace until someone sets one - pays nothing. Asking for the tier report with no
    ///     configuration must therefore be safe and empty, not an attempt to enumerate a null root.
    /// </remarks>
    [Fact]
    public void GameAssetResolver_WithNoGamePathConfigured_ReportsNoShippedAssets()
    {
        using var provider = BuildProviderWithHostStubs();

        var tiers = provider.GetRequiredService<IGameAssetResolver>().Tiers;

        Assert.False(tiers.CanResolveShippedAssets);
        Assert.Equal(0, tiers.ArchiveCount);
    }
}
