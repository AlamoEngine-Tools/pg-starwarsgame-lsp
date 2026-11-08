// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using PG.StarWarsGame.LSP.Assets.Projection;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

/// <summary>
///     Reads the shipped factions out of the files the FactionFiles registry lists.
/// </summary>
/// <remarks>
///     The engine keeps factions in a manager PG.StarWarsGame.Engine does not expose, so the builder
///     reads them the way it reads music events and shadow blob materials: from the game's own XML,
///     through the same reader the indexer uses for the registry.
/// </remarks>
public sealed class FactionReaderTest
{
    private const string Registry = "DATA\\XML\\FACTIONFILES.XML";

    private static Func<string, Stream?> Files(params (string Path, string Content)[] files)
    {
        var map = files.ToDictionary(f => f.Path, f => f.Content, StringComparer.OrdinalIgnoreCase);
        return path => map.TryGetValue(path, out var content)
            ? new MemoryStream(Encoding.UTF8.GetBytes(content))
            : null;
    }

    [Fact]
    public void Read_ProjectsEveryFactionOfEveryListedFile_InListOrder()
    {
        var open = Files(
            (Registry, "<Faction_Files><File>Factions.xml</File><File>Expansion_Factions.xml</File></Faction_Files>"),
            ("DATA\\XML\\FACTIONS.XML", """
                                        <Factions>
                                          <Faction Name="Rebel">
                                            <Text_ID>TEXT_FACTION_REBEL</Text_ID>
                                          </Faction>
                                          <Faction Name="Empire" />
                                        </Factions>
                                        """),
            ("DATA\\XML\\EXPANSION_FACTIONS.XML", """<Factions><Faction Name="Underworld"></Faction></Factions>"""));

        var result = FactionReader.Read(Registry, open);

        Assert.Equal(["Rebel", "Empire", "Underworld"], result.Select(e => e.Name));
        Assert.All(result, e => Assert.Equal("FACTION", e.ClassificationName));
    }

    [Fact]
    public void Read_CarriesTheFileAndThePrintableLineOfEachFaction()
    {
        var open = Files(
            (Registry, "<Faction_Files><File>Factions.xml</File></Faction_Files>"),
            ("DATA\\XML\\FACTIONS.XML", """
                                        <Factions>

                                          <Faction Name="Rebel">
                                          </Faction>
                                        </Factions>
                                        """));

        var entry = Assert.Single(FactionReader.Read(Registry, open));

        Assert.Equal("DATA\\XML\\FACTIONS.XML", entry.Location.XmlFile);
        Assert.Equal(3, entry.Location.Line);
    }

    [Fact]
    public void Read_ListedFileThatDoesNotShip_IsSkippedAndReported()
    {
        var problems = new List<string>();
        var open = Files(
            (Registry, "<Faction_Files><File>Factions.xml</File><File>Expansion_Factions.xml</File></Faction_Files>"),
            ("DATA\\XML\\FACTIONS.XML", """<Factions><Faction Name="Rebel" /></Factions>"""));

        var result = FactionReader.Read(Registry, open, problems.Add);

        Assert.Equal(["Rebel"], result.Select(e => e.Name));
        var problem = Assert.Single(problems);
        Assert.Contains("EXPANSION_FACTIONS.XML", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_NoRegistry_ReturnsNothingAndReportsIt()
    {
        var problems = new List<string>();

        var result = FactionReader.Read(Registry, Files(), problems.Add);

        Assert.Empty(result);
        Assert.Contains(problems, p => p.Contains("FACTIONFILES.XML", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Read_ElementWithoutAName_IsSkipped()
    {
        var open = Files(
            (Registry, "<Faction_Files><File>Factions.xml</File></Faction_Files>"),
            ("DATA\\XML\\FACTIONS.XML", """<Factions><Faction /><Faction Name="Rebel" /></Factions>"""));

        Assert.Equal(["Rebel"], FactionReader.Read(Registry, open).Select(e => e.Name));
    }
}