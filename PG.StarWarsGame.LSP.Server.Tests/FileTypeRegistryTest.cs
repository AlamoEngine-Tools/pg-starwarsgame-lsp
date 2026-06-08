// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class FileTypeRegistryTest
{
    private static FileTypeRegistry Build()
    {
        return new FileTypeRegistry();
    }

    [Fact]
    public void GetTypesForFile_UnknownPath_ReturnsEmpty()
    {
        var registry = Build();

        var result = registry.GetTypesForFile("data/xml/unknown.xml");

        Assert.Empty(result);
    }

    [Fact]
    public void RegisterFile_ThenGet_ReturnsRegisteredTypes()
    {
        var registry = Build();
        var types = ImmutableArray.Create("GameObjectType");

        registry.RegisterFile("data/xml/hardpoints.xml", types);
        var result = registry.GetTypesForFile("data/xml/hardpoints.xml");

        Assert.Equal(["GameObjectType"], result.ToArray());
    }

    [Fact]
    public void GetTypesForFile_CaseInsensitive()
    {
        var registry = Build();
        registry.RegisterFile("data/xml/hardpoints.xml", ImmutableArray.Create("GameObjectType"));

        Assert.Equal(["GameObjectType"], registry.GetTypesForFile("DATA/XML/HARDPOINTS.XML").ToArray());
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile("Data/Xml/Hardpoints.xml").ToArray());
    }

    [Fact]
    public void UnregisterFile_RemovesEntry()
    {
        var registry = Build();
        registry.RegisterFile("data/xml/hardpoints.xml", ImmutableArray.Create("GameObjectType"));

        registry.UnregisterFile("data/xml/hardpoints.xml");

        Assert.Empty(registry.GetTypesForFile("data/xml/hardpoints.xml"));
    }

    [Fact]
    public void RegisterFile_OverwritesPreviousEntry()
    {
        var registry = Build();
        registry.RegisterFile("data/xml/factions.xml", ImmutableArray.Create("Faction"));
        registry.RegisterFile("data/xml/factions.xml", ImmutableArray.Create("Campaign"));

        Assert.Equal(["Campaign"], registry.GetTypesForFile("data/xml/factions.xml").ToArray());
    }

    [Fact]
    public void All_ReflectsCurrentState()
    {
        var registry = Build();
        registry.RegisterFile("data/xml/hardpoints.xml", ImmutableArray.Create("GameObjectType"));
        registry.RegisterFile("data/xml/movies.xml", ImmutableArray.Create("BinkMovie"));

        Assert.Equal(2, registry.All.Count);
    }

    // ── canonical file:/// URI contract ──────────────────────────────────────
    // WorkspaceIndexer always calls IFileHelper.PathToFileUri() before registering,
    // so real keys are canonical file:/// URIs (lowercase, forward-slash).

    [Fact]
    public void RegisterFile_CanonicalUri_ThenGet_ReturnsTypes()
    {
        var registry = Build();

        registry.RegisterFile("file:///c:/game/data/xml/units.xml", ImmutableArray.Create("GameObjectType"));
        var result = registry.GetTypesForFile("file:///c:/game/data/xml/units.xml");

        Assert.Equal(["GameObjectType"], result.ToArray());
    }

    [Fact]
    public void GetTypesForFile_CanonicalKey_MixedCaseLookup_ReturnsTypes()
    {
        // The registry is OrdinalIgnoreCase so a caller that has not yet normalized
        // the URI will still get a match. (IsStoryParserDocument normalizes first,
        // but a defensive lookup with mixed case must not silently return empty.)
        var registry = Build();
        registry.RegisterFile("file:///c:/game/data/xml/units.xml", ImmutableArray.Create("GameObjectType"));

        var result = registry.GetTypesForFile("file:///C:/Game/Data/XML/Units.xml");

        Assert.Equal(["GameObjectType"], result.ToArray());
    }
}