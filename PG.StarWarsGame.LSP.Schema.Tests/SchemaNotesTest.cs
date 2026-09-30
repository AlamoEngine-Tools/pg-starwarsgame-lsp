// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     The one annotation channel: every schema element carries a list of kinded, localised notes,
///     and the kind - not the file - decides how loudly each one is said.
/// </summary>
public sealed class SchemaNotesTest
{
    // ── The kind ranking ────────────────────────────────────────────────────

    // The rank is the enum's own value, declared explicitly with gaps so that inserting a kind
    // later cannot renumber the rest and quietly change what the editor shows.
    [Fact]
    public void NoteKinds_RankFromMostToLeastActionable()
    {
        Assert.Equal(
            new[]
            {
                SchemaNoteKind.BuggedInEngine, SchemaNoteKind.Deprecated, SchemaNoteKind.Untested,
                SchemaNoteKind.Remark, SchemaNoteKind.Since
            },
            Enum.GetValues<SchemaNoteKind>().OrderBy(k => (int)k).ToArray());
    }

    [Fact]
    public void NoteKinds_HaveDistinctRanks_WithRoomToInsert()
    {
        var ranks = Enum.GetValues<SchemaNoteKind>().Select(k => (int)k).ToList();

        Assert.Equal(ranks.Count, ranks.Distinct().Count());
        Assert.All(ranks.Zip(ranks.Skip(1)), pair => Assert.True(pair.Second - pair.First > 1,
            $"ranks {pair.First} and {pair.Second} are adjacent - leave a gap to insert a kind"));
    }

    // ── Ordering ────────────────────────────────────────────────────────────

    [Fact]
    public void EnumValueNotes_AreRankedByKind_NotByWrittenOrder()
    {
        const string yaml = """
                            name: StoryFlagCompareMethod
                            values:
                              - name: NOT_EQUAL_TO
                                notes:
                                  - kind: Remark
                                    text:
                                      en: "Only ever seen in one mod."
                                  - kind: BuggedInEngine
                                    text:
                                      en: "Its branch does nothing, so the event never fires."
                            """;

        var value = Assert.Single(YamlSchemaParser.ParseEnumFile(yaml).Values);

        Assert.Equal(
            new[] { SchemaNoteKind.BuggedInEngine, SchemaNoteKind.Remark },
            value.Notes.Select(n => n.Kind).ToArray());
        Assert.Equal("Its branch does nothing, so the event never fires.", value.Notes[0].Text["en"]);
    }

    // Written order is the tie-break and nothing else: two engine bugs on one element stay in the
    // order someone thought to write them.
    [Fact]
    public void NotesOfTheSameKind_KeepTheirWrittenOrder()
    {
        const string yaml = """
                            name: PlanetStatisticsType
                            values:
                              - name: MAX_STARBASE_LEVEL
                                notes:
                                  - kind: BuggedInEngine
                                    text:
                                      en: "First."
                                  - kind: BuggedInEngine
                                    text:
                                      en: "Second."
                            """;

        var value = Assert.Single(YamlSchemaParser.ParseEnumFile(yaml).Values);

        Assert.Equal(new[] { "First.", "Second." }, value.Notes.Select(n => n.Text["en"]).ToArray());
    }

    // ── Shape ───────────────────────────────────────────────────────────────

    [Fact]
    public void ANote_CarriesEveryLanguageItWasGiven()
    {
        const string yaml = """
                            name: StoryEventType
                            notes:
                              - kind: Remark
                                text:
                                  en: "FoC-only enum."
                                  de: "Nur in FoC."
                            values: []
                            """;

        var note = Assert.Single(YamlSchemaParser.ParseEnumFile(yaml).Notes);

        Assert.Equal("FoC-only enum.", note.Text["en"]);
        Assert.Equal("Nur in FoC.", note.Text["de"]);
    }

    // Since is the one kind whose point is a value rather than a sentence.
    [Fact]
    public void ASinceNote_CarriesItsVersion()
    {
        const string yaml = """
                            name: StoryEventType
                            values:
                              - name: STORY_CORRUPTION_INCREASED
                                notes:
                                  - kind: Since
                                    value: "1.1"
                            """;

        var note = Assert.Single(Assert.Single(YamlSchemaParser.ParseEnumFile(yaml).Values).Notes);

        Assert.Equal(SchemaNoteKind.Since, note.Kind);
        Assert.Equal("1.1", note.Value);
    }

    [Fact]
    public void AnElementWithoutNotes_HasNone()
    {
        const string yaml = """
                            name: StoryEventType
                            values:
                              - name: STORY_FLAG
                            """;

        var def = YamlSchemaParser.ParseEnumFile(yaml);

        Assert.Empty(def.Notes);
        Assert.Empty(Assert.Single(def.Values).Notes);
    }

    // ── Every element that has a description has notes ──────────────────────

    [Fact]
    public void ATag_CarriesNotes()
    {
        const string yaml = """
                            tags:
                              - tag: Event_Type
                                type: DynamicEnumValue
                                notes:
                                  - kind: Deprecated
                                    text:
                                      en: "Use Event_Kind."
                            """;

        var note = Assert.Single(Assert.Single(YamlSchemaParser.ParseTagFile(yaml)).Notes);

        Assert.Equal(SchemaNoteKind.Deprecated, note.Kind);
    }

    [Fact]
    public void AParamSlot_CarriesNotes()
    {
        const string yaml = """
                            name: StoryEventType
                            values:
                              - name: STORY_FLAG
                                params:
                                  - position: 2
                                    type: DynamicEnumValue
                                    enumName: StoryFlagCompareMethod
                                    notes:
                                      - kind: Untested
                                        text:
                                          en: "Not checked against the corpus."
                            """;

        var param = Assert.Single(Assert.Single(YamlSchemaParser.ParseEnumFile(yaml).Values).Params!);

        Assert.Equal(SchemaNoteKind.Untested, Assert.Single(param.Notes).Kind);
    }

    [Fact]
    public void AHardcodedSetAndItsValues_CarryNotes()
    {
        const string yaml = """
                            name: BehaviorModule
                            notes:
                              - kind: Remark
                                text:
                                  en: "The engine's own list."
                            values:
                              - name: DUMMY_GROUND_COMPANY
                                notes:
                                  - kind: Deprecated
                                    text:
                                      en: "Gone in FoC."
                            """;

        var set = YamlSchemaParser.ParseHardcodedSetFile(yaml);

        Assert.Equal(SchemaNoteKind.Remark, Assert.Single(set.Notes).Kind);
        Assert.Equal(SchemaNoteKind.Deprecated, Assert.Single(Assert.Single(set.Values).Notes).Kind);
    }

    [Fact]
    public void AnObjectKind_CarriesNotes()
    {
        const string yaml = """
                            kinds:
                              - kind: HeroCompany
                                behaviors: [DUMMY_GROUND_COMPANY]
                                notes:
                                  - kind: Untested
                                    text:
                                      en: "Predicate not verified on foc."
                            """;

        var kind = Assert.Single(YamlSchemaParser.ParseKindFile(yaml));

        Assert.Equal(SchemaNoteKind.Untested, Assert.Single(kind.Notes).Kind);
    }

    [Fact]
    public void AnObjectTypeAndAMetafile_CarryNotes()
    {
        const string types = """
                             types:
                               - typeName: SpaceUnit
                                 notes:
                                   - kind: Remark
                                     text:
                                       en: "Also used for starbases."
                             """;
        const string metafiles = """
                                 metafiles:
                                   - path: Data/XML/GameObjectFiles.xml
                                     metaFileType: fileRegistry
                                     notes:
                                       - kind: Remark
                                         text:
                                           en: "First found wins."
                                 """;

        Assert.Equal(SchemaNoteKind.Remark,
            Assert.Single(Assert.Single(YamlSchemaParser.ParseTypeFile(types)).Notes).Kind);
        Assert.Equal(SchemaNoteKind.Remark,
            Assert.Single(Assert.Single(YamlSchemaParser.ParseMetafileFile(metafiles)).Notes).Kind);
    }

    // ── The forms that are gone ─────────────────────────────────────────────

    // A bare locale map was how every note was written before 2.0.0. It is not read as an untagged
    // remark: a note whose kind nobody chose is exactly what this change exists to remove, and a
    // schema still using the old shape must say so loudly rather than load with its notes silently
    // demoted.
    [Fact]
    public void ABareNotesMap_IsRejected()
    {
        const string yaml = """
                            name: StoryEventType
                            values:
                              - name: STORY_FLAG
                                notes:
                                  en: "The old shape."
                            """;

        Assert.ThrowsAny<Exception>(() => YamlSchemaParser.ParseEnumFile(yaml));
    }

    [Fact]
    public void AnUnknownNoteKind_IsRejected()
    {
        const string yaml = """
                            name: StoryEventType
                            values:
                              - name: STORY_FLAG
                                notes:
                                  - kind: SomethingFromAFutureSchema
                                    text:
                                      en: "..."
                            """;

        Assert.ThrowsAny<Exception>(() => YamlSchemaParser.ParseEnumFile(yaml));
    }
}