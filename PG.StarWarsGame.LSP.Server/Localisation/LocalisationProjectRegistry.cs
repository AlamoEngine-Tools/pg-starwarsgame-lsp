// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class LocalisationProjectRegistry : ILocalisationProjectRegistry
{
    private volatile IReadOnlyList<LocProjectInfo> _projects = [];

    public IReadOnlyList<LocProjectInfo> Projects => _projects;

    public void Set(IReadOnlyList<LocProjectInfo> projects)
    {
        _projects = projects;
    }
}
