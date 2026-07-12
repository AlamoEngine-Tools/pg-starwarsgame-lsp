// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Story.Tests;

internal sealed class FakeLspConfigurationProvider : ILspConfigurationProvider
{
    public LspConfiguration Current { get; set; } = new();

    public void LoadFrom(object? initializationOptions)
    {
    }

    public static FakeLspConfigurationProvider WithFeatures(FeatureFlags features)
    {
        return new FakeLspConfigurationProvider { Current = new LspConfiguration { Features = features } };
    }
}
