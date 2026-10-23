// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using AET.SteamAbstraction;
using AnakinRaW.CommonUtilities.Registry;
using AnakinRaW.CommonUtilities.Registry.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.Infrastructure;
using PG.StarWarsGame.Infrastructure.Clients.Steam;
using PG.StarWarsGame.Infrastructure.Games;
using PG.StarWarsGame.Infrastructure.Games.Registry;
using PG.StarWarsGame.Infrastructure.Services.Detection;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Finds the installs through PetroglyphTools' game detectors, as ModVerify does: the Steam
///     library first, then, on Windows, the game's registry keys. A failed detector answers nothing
///     rather than stopping the server.
/// </summary>
public sealed class GameInstallLocator(ILogger<GameInstallLocator> logger) : IGameInstallLocator
{
    private readonly Lazy<ServiceProvider> _services = new(CreateServices);

    public string? FindBaseGame()
    {
        return Find(GameType.Eaw);
    }

    public string? FindExpansion()
    {
        return Find(GameType.Foc);
    }

    private string? Find(GameType type)
    {
        try
        {
            var services = _services.Value;
            var detectors = new List<IGameDetector> { new SteamPetroglyphStarWarsGameDetector(services) };
            if (OperatingSystem.IsWindows())
            {
                var registries = services.GetRequiredService<IGameRegistryFactory>();
                detectors.Add(new RegistryGameDetector(registries.CreateRegistry(GameType.Eaw),
                    registries.CreateRegistry(GameType.Foc), false, services));
            }

            return new CompositeGameDetector(detectors, services, true).Detect(type).GameLocation?.FullName;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Detecting the {Game} install failed", type);
            return null;
        }
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.AddSingleton<IRegistry>(OperatingSystem.IsWindows()
            ? new WindowsRegistry()
            : new InMemoryRegistry(InMemoryRegistryCreationFlags.WindowsLike));
        SteamAbstractionLayer.InitializeServices(services);
        PetroglyphGameInfrastructure.InitializeServices(services);
        SteamPetroglyphStarWarsGameClients.InitializeServices(services);
        return services.BuildServiceProvider();
    }
}
