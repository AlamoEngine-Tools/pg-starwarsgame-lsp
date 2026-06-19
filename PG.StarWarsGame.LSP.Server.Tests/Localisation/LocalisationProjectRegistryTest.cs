// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class LocalisationProjectRegistryTest
{
    [Fact]
    public void Projects_BeforeSet_IsEmpty()
    {
        var registry = new LocalisationProjectRegistry();
        Assert.Empty(registry.Projects);
    }

    [Fact]
    public void Set_SingleProject_ProjectIsReturned()
    {
        var registry = new LocalisationProjectRegistry();
        var info = new LocProjectInfo("MasterTextFile.csv", "/mod/Data/Text/MasterTextFile.csv", "Csv");

        registry.Set([info]);

        Assert.Single(registry.Projects);
        Assert.Equal("MasterTextFile.csv", registry.Projects[0].Label);
        Assert.Equal("/mod/Data/Text/MasterTextFile.csv", registry.Projects[0].FilePath);
        Assert.Equal("Csv", registry.Projects[0].ResourceType);
    }

    [Fact]
    public void Set_MultipleProjects_AllReturned()
    {
        var registry = new LocalisationProjectRegistry();
        var projects = new[]
        {
            new LocProjectInfo("a.csv", "/mod/a.csv", "Csv"),
            new LocProjectInfo("b.csv", "/mod/b.csv", "Csv")
        };

        registry.Set(projects);

        Assert.Equal(2, registry.Projects.Count);
    }

    [Fact]
    public void Set_ReplacesExistingProjects()
    {
        var registry = new LocalisationProjectRegistry();
        registry.Set([new LocProjectInfo("first.csv", "/mod/first.csv", "Csv")]);
        registry.Set([]);

        Assert.Empty(registry.Projects);
    }
}