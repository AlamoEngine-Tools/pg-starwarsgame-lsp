// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     What <c>Deprecated</c> is allowed to mean in the shipped schema.
///     <para>
///         Deprecation says "this still works, stop using it". Schema 1.x had one boolean for
///         everything worth flagging, so it also carried "this does not work" and "nobody has
///         checked" - and the 2.0.0 sweep turned every one of them into <c>kind: Deprecated</c>
///         mechanically, which preserved the conflation instead of resolving it.
///     </para>
///     <para>
///         The entries below were re-kinded from what their own note text already said. These tests
///         pin that, so the distinction cannot quietly collapse again.
///     </para>
/// </summary>
public sealed class EawSchemaDeprecationKindsTest
{
    /// <summary>Every note in the shipped schema, with the name of whatever carries it.</summary>
    private static IReadOnlyList<(string Owner, string File, SchemaNote Note)> AllNotes()
    {
        var found = new List<(string, string, SchemaNote)>();

        foreach (var rel in EawSchemaRepo.YamlFiles("enums"))
        {
            var def = YamlSchemaParser.ParseEnumFile(EawSchemaRepo.Read(rel));
            foreach (var value in def.Values)
            foreach (var note in value.Notes)
                found.Add((value.Name, rel, note));
        }

        foreach (var rel in EawSchemaRepo.YamlFiles("hardcoded"))
        {
            var set = YamlSchemaParser.ParseHardcodedSetFile(EawSchemaRepo.Read(rel));
            foreach (var value in set.Values)
            foreach (var note in value.Notes)
                found.Add((value.Name, rel, note));
        }

        foreach (var rel in EawSchemaRepo.YamlFiles("tags"))
        {
            foreach (var tag in YamlSchemaParser.ParseTagFile(EawSchemaRepo.Read(rel)))
            foreach (var note in tag.Notes)
                found.Add((tag.Tag, rel, note));
        }

        return found;
    }

    private static IReadOnlyList<SchemaNoteKind> KindsOf(string owner)
    {
        return [.. AllNotes().Where(n => n.Owner == owner).Select(n => n.Note.Kind)];
    }

    /// <summary>
    ///     The two cannot both be true of one element: bugged says it does not work, deprecated says
    ///     it does. <c>STORY_POLITICAL_CONTROL</c> carried both.
    /// </summary>
    [Fact]
    public void NothingIsBothDeprecatedAndBuggedInEngine()
    {
        var contradictory = AllNotes()
            .GroupBy(n => (n.Owner, n.File))
            .Where(g => g.Any(n => n.Note.Kind == SchemaNoteKind.Deprecated)
                        && g.Any(n => n.Note.Kind == SchemaNoteKind.BuggedInEngine))
            .Select(g => $"{g.Key.File}: {g.Key.Owner}")
            .ToList();

        Assert.True(contradictory.Count == 0,
            "Deprecated says it still works, BuggedInEngine says it does not:\n  " +
            string.Join("\n  ", contradictory));
    }

    /// <summary>
    ///     Rewards whose own note says the engine does not run them. "Disabled in the game engine",
    ///     "routed to a stub which only logs", "does not do anything" are statements of
    ///     non-function, which is an error, not a style warning.
    /// </summary>
    [Theory]
    [InlineData("DISABLE_AUTORESOLVE")]
    [InlineData("ENABLE_AUTORESOLVE")]
    [InlineData("SET_WEATHER")]
    [InlineData("STORY_POLITICAL_CONTROL")]
    public void AnEntryTheEngineDoesNotRunIsBuggedNotDeprecated(string name)
    {
        var kinds = KindsOf(name);

        Assert.Contains(SchemaNoteKind.BuggedInEngine, kinds);
        Assert.DoesNotContain(SchemaNoteKind.Deprecated, kinds);
    }

    /// <summary>
    ///     Behaviour modules whose note said "marked deprecated pending confirmation it is
    ///     functional". That is nobody having checked, which is exactly <c>Untested</c> - using
    ///     deprecation as a placeholder for uncertainty is what the kinds exist to stop.
    /// </summary>
    [Theory]
    [InlineData("BOARDABLE")]
    [InlineData("SIMPLE_LOCOMOTOR")]
    [InlineData("STARSHIP_COMBATANT")]
    [InlineData("STARSHIP_LOCOMOTOR")]
    public void AnUnverifiedBehaviourModuleIsUntestedNotDeprecated(string name)
    {
        var kinds = KindsOf(name);

        Assert.Contains(SchemaNoteKind.Untested, kinds);
        Assert.DoesNotContain(SchemaNoteKind.Deprecated, kinds);
    }

    /// <summary>
    ///     A deprecation with no words tells the author a tag is retired and nothing else - and
    ///     gives nobody a way to check whether that is even true. It was not, for the last two:
    ///     the engine validates <c>Destroy_Starbase</c> and <c>Destroy_Land_Base</c> at load and
    ///     logs an error when NEITHER is set, so they were never retired at all.
    /// </summary>
    [Fact]
    public void NoDeprecationIsLeftWithoutAReason()
    {
        var wordless = AllNotes()
            .Where(n => n.Note.Kind == SchemaNoteKind.Deprecated && n.Note.Text.Count == 0)
            .Select(n => $"{n.File}: {n.Owner}")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(wordless.Count == 0,
            "A deprecation with no words cannot be checked or acted on:\n  " +
            string.Join("\n  ", wordless));
    }

    /// <summary>
    ///     Measured in the shipped engine: the base-destruction ability is validated at load and
    ///     logs an error when neither flag is set, because the ability then does nothing. Both were
    ///     marked deprecated with no reason given; they are ordinary, live tags.
    /// </summary>
    [Theory]
    [InlineData("Destroy_Starbase")]
    [InlineData("Destroy_Land_Base")]
    public void TheBaseDestructionFlagsAreNotDeprecated(string tag)
    {
        Assert.DoesNotContain(SchemaNoteKind.Deprecated, KindsOf(tag));
    }
}