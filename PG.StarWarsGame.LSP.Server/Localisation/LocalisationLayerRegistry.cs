// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class LocalisationLayerRegistry : ILocalisationLayerRegistry
{
    private volatile IReadOnlyList<LocalisationLayerEntry> _layers = [];

    public IReadOnlyList<LocalisationLayerEntry> Layers => _layers;

    public void Set(IReadOnlyList<LocalisationLayerEntry> layers)
    {
        _layers = layers;
    }
}