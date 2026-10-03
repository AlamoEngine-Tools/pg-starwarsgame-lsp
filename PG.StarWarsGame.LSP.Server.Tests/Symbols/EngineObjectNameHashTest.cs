// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using PG.Commons.Hashing;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Server.Symbols;

namespace PG.StarWarsGame.LSP.Server.Tests.Symbols;

/// <summary>
///     The hash the engine files a game object under: CRC-32 of the upper-cased name. Measured: the
///     object lookup upper-cases the name, takes a standard CRC-32 of it and finds the object by that
///     number alone.
/// </summary>
public sealed class EngineObjectNameHashTest
{
    private static EngineObjectNameHash Hash()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new MockFileSystem());
        services.SupportPetroglyphHashing();
        services.SupportLocalisationBaseline();
        return new EngineObjectNameHash(services.BuildServiceProvider().GetRequiredService<ICrc32HashingService>());
    }

    [Fact]
    public void IsTheStandardCrc32_OfTheUpperCasedName()
    {
        Assert.Equal(0xA3830348u, Hash().Of("abc"));
    }

    [Fact]
    public void IgnoresCase_AsTheEngineDoes()
    {
        var hash = Hash();

        Assert.Equal(hash.Of("Darth_Vader"), hash.Of("DARTH_VADER"));
    }

    [Fact]
    public void TwoDifferentNames_CanShareOne()
    {
        // The pair the E2E fixture uses - found by search, both 0xC492BBC0.
        var hash = Hash();

        Assert.Equal(0xC492BBC0u, hash.Of("E2E_CRC_BXO9XL"));
        Assert.Equal(0xC492BBC0u, hash.Of("e2e_crc_cdatba"));
    }
}
