// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using PG.StarWarsGame.LSP.Assets.Projection;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

/// <summary>
///     Reads each shipped object's kind facts back out of the game's own XML.
/// </summary>
/// <remarks>
///     The engine's object model carries models, icons and the variant base, but neither behaviours
///     nor the hero flags, so projecting them means going to the file the object came from. Every
///     shipped object knows its own file, so the files are opened once each.
/// </remarks>
public sealed class GameObjectFactsReaderTest
{
    private static readonly string[] HeroFlags = ["Is_Named_Hero", "Is_Generic_Hero"];

    private static Func<string, Stream?> Files(params (string Path, string Content)[] files)
    {
        var map = files.ToDictionary(f => f.Path, f => f.Content, StringComparer.OrdinalIgnoreCase);
        return path => map.TryGetValue(path, out var content)
            ? new MemoryStream(Encoding.UTF8.GetBytes(content))
            : null;
    }

    [Fact]
    public void Read_TakesEachObjectsBehavioursFromItsOwnFile()
    {
        var open = Files(("DATA\\XML\\PLANETS.XML", """
                                                    <Planets>
                                                      <Planet Name="Alderaan">
                                                        <Behavior>SELECTABLE, PLANET, PRODUCTION</Behavior>
                                                      </Planet>
                                                      <Planet Name="Hoth">
                                                        <Behavior>PLANET</Behavior>
                                                        <SpaceBehavior>SELECTABLE</SpaceBehavior>
                                                      </Planet>
                                                    </Planets>
                                                    """));

        var result = GameObjectFactsReader.Read(
            [("Alderaan", "DATA\\XML\\PLANETS.XML"), ("Hoth", "DATA\\XML\\PLANETS.XML")], open);

        Assert.Equal(["SELECTABLE", "PLANET", "PRODUCTION"], result["Alderaan"].Behaviors);
        Assert.Equal(["PLANET", "SELECTABLE"], result["Hoth"].Behaviors);
    }

    [Fact]
    public void Read_ObjectWithNoBehaviourTag_IsAbsent()
    {
        var open = Files(("U.XML", """
                                   <Units><Unit Name="PROP"><Max_Health>1</Max_Health></Unit></Units>
                                   """));

        var result = GameObjectFactsReader.Read([("PROP", "U.XML")], open);

        Assert.Empty(result);
    }

    // ── the tracked flags ────────────────────────────────────────────────────

    [Fact]
    public void Read_RecordsATrackedFlagThatIsTrue()
    {
        var open = Files(("U.XML", """
                                   <Units>
                                     <Unit Name="Vader">
                                       <Behavior>DUMMY_STARSHIP</Behavior>
                                       <Is_Named_Hero>Yes</Is_Named_Hero>
                                     </Unit>
                                   </Units>
                                   """));

        var result = GameObjectFactsReader.Read([("Vader", "U.XML")], open, HeroFlags);

        Assert.Equal(["Is_Named_Hero"], result["Vader"].Flags);
    }

    // "No" is not a hero. An inspected object with none gets an empty list, which is a different
    // answer from a symbol nobody looked at, and the kind matcher depends on telling them apart.
    [Fact]
    public void Read_TrackedFlagThatIsFalse_IsNotRecorded()
    {
        var open = Files(("U.XML", """
                                   <Units>
                                     <Unit Name="Trooper">
                                       <Behavior>IDLE</Behavior>
                                       <Is_Named_Hero>No</Is_Named_Hero>
                                     </Unit>
                                   </Units>
                                   """));

        var result = GameObjectFactsReader.Read([("Trooper", "U.XML")], open, HeroFlags);

        Assert.Empty(result["Trooper"].Flags);
    }

    // Only what some kind asks about. Every other true boolean on an object is most of its tags,
    // carried for nobody.
    [Fact]
    public void Read_UntrackedBoolean_IsIgnored()
    {
        var open = Files(("U.XML", """
                                   <Units>
                                     <Unit Name="Trooper">
                                       <Behavior>IDLE</Behavior>
                                       <Show_Name>Yes</Show_Name>
                                     </Unit>
                                   </Units>
                                   """));

        var result = GameObjectFactsReader.Read([("Trooper", "U.XML")], open, HeroFlags);

        Assert.Empty(result["Trooper"].Flags);
    }

    // ── file handling ────────────────────────────────────────────────────────

    // One open per file, however many objects come from it - a shipped install has thousands of
    // objects across a couple of hundred files, and opening through the MEG layer is not free.
    [Fact]
    public void Read_OpensEachFileOnce()
    {
        var opens = new List<string>();
        var inner = Files(("U.XML", """
                                    <Units>
                                      <Unit Name="A"><Behavior>IDLE</Behavior></Unit>
                                      <Unit Name="B"><Behavior>IDLE</Behavior></Unit>
                                    </Units>
                                    """));

        var result = GameObjectFactsReader.Read(
            [("A", "U.XML"), ("B", "U.XML")],
            path =>
            {
                opens.Add(path);
                return inner(path);
            });

        Assert.Equal(2, result.Count);
        Assert.Equal(["U.XML"], opens);
    }

    [Fact]
    public void Read_MissingFile_IsSkipped()
    {
        var result = GameObjectFactsReader.Read([("A", "GONE.XML")], Files());

        Assert.Empty(result);
    }

    // A shipped file that will not parse must cost that file, not the run. The baseline is built
    // from whatever the game actually holds, and one malformed file is not a reason to ship none.
    [Fact]
    public void Read_MalformedFile_IsSkippedAndReported()
    {
        var problems = new List<string>();
        var open = Files(("BAD.XML", "<Units><Unit Name=\"A\"><Behavior>IDLE</Units>"));

        var result = GameObjectFactsReader.Read([("A", "BAD.XML")], open, onProblem: problems.Add);

        Assert.Empty(result);
        Assert.Contains("BAD.XML", Assert.Single(problems));
    }

    [Fact]
    public void Read_ObjectWithoutAFile_IsSkipped()
    {
        var result = GameObjectFactsReader.Read([("A", null), ("B", "")], Files());

        Assert.Empty(result);
    }

    // The engine matches object names case-insensitively, and the corpus is not consistent about
    // which case a reference uses.
    [Fact]
    public void Read_MatchesObjectNamesCaseInsensitively()
    {
        var open = Files(("U.XML", """
                                   <Units><Unit Name="Alderaan"><Behavior>PLANET</Behavior></Unit></Units>
                                   """));

        var result = GameObjectFactsReader.Read([("ALDERAAN", "U.XML")], open);

        Assert.Equal(["PLANET"], result["alderaan"].Behaviors);
    }

    // Objects nest one level down in some shipped files, so a flat pass over the root's children
    // would find nothing for them.
    [Fact]
    public void Read_FindsObjectsAtAnyDepth()
    {
        var open = Files(("U.XML", """
                                   <GameObjectFiles>
                                     <Container>
                                       <SpaceUnit Name="X_Wing"><Behavior>DUMMY_STARSHIP</Behavior></SpaceUnit>
                                     </Container>
                                   </GameObjectFiles>
                                   """));

        var result = GameObjectFactsReader.Read([("X_Wing", "U.XML")], open);

        Assert.Equal(["DUMMY_STARSHIP"], result["X_Wing"].Behaviors);
    }
}