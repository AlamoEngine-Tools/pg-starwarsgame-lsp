// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

public sealed class XmlDocumentFactProducerTest
{
    private const string Uri = "file:///units/SpaceUnitData.xml";

    private static XmlDocumentFactProducer Build(
        ISchemaProvider? schema = null,
        IFileTypeRegistry? registry = null,
        IXmlStructuralValidator? validator = null,
        IEnumerable<IXmlCrossTagRule>? crossTagRules = null)
    {
        return new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            schema ?? new SingleTagSchemaProvider(),
            registry ?? new EmptyFileTypeRegistry(),
            validator ?? new XmlStructuralValidator(),
            crossTagRules ?? []);
    }

    // ── tag value facts ───────────────────────────────────────────────────────

    [Fact]
    public void Non_empty_leaf_value_emits_XmlTagValueFact()
    {
        const string xml = "<GameObjectFiles><SpaceUnit><Max_Speed>10.0</Max_Speed></SpaceUnit></GameObjectFiles>";
        var facts = Build().Produce(xml, Uri);
        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal("Max_Speed", tvf.Tag.Tag, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("10.0", tvf.RawValue);
        Assert.Equal(Uri, tvf.DocumentUri);
    }

    [Fact]
    public void Empty_leaf_value_does_not_emit_XmlTagValueFact()
    {
        const string xml = "<GameObjectFiles><SpaceUnit><Max_Speed></Max_Speed></SpaceUnit></GameObjectFiles>";
        var facts = Build().Produce(xml, Uri);
        Assert.Empty(facts.OfType<XmlTagValueFact>());
    }

    [Fact]
    public void Whitespace_only_leaf_value_does_not_emit_XmlTagValueFact()
    {
        const string xml = "<GameObjectFiles><SpaceUnit><Max_Speed>   </Max_Speed></SpaceUnit></GameObjectFiles>";
        var facts = Build().Produce(xml, Uri);
        Assert.Empty(facts.OfType<XmlTagValueFact>());
    }

    [Fact]
    public void Unknown_tag_does_not_emit_XmlTagValueFact()
    {
        const string xml = "<GameObjectFiles><SpaceUnit><Unknown_Tag>42</Unknown_Tag></SpaceUnit></GameObjectFiles>";
        var facts = Build(new EmptySchemaProvider()).Produce(xml, Uri);
        Assert.Empty(facts.OfType<XmlTagValueFact>());
    }

    [Fact]
    public void XmlTagValueFact_carries_correct_tag_definition()
    {
        const string xml = "<Root><Max_Speed>5.0</Max_Speed></Root>";
        var facts = Build().Produce(xml, Uri);
        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal(XmlValueType.Float, tvf.Tag.ValueType);
    }

    // ── duplicate tag facts ───────────────────────────────────────────────────

    [Fact]
    public void Singleton_tag_appearing_twice_emits_XmlDuplicateTagFact_for_each_occurrence()
    {
        const string xml = "<Root><Max_Speed>1.0</Max_Speed><Max_Speed>2.0</Max_Speed></Root>";
        var facts = Build().Produce(xml, Uri);
        var dups = facts.OfType<XmlDuplicateTagFact>().ToList();
        Assert.Equal(2, dups.Count);
    }

    [Fact]
    public void XmlDuplicateTagFact_references_other_lines()
    {
        const string xml = "<Root>\n<Max_Speed>1.0</Max_Speed>\n<Max_Speed>2.0</Max_Speed>\n</Root>";
        var facts = Build().Produce(xml, Uri);
        var dups = facts.OfType<XmlDuplicateTagFact>().ToList();
        Assert.Equal(2, dups.Count);
        // Each occurrence records the other's line
        Assert.All(dups, d => Assert.Single(d.OtherLines));
        Assert.NotEqual(dups[0].OtherLines[0], dups[1].OtherLines[0]);
    }

    [Fact]
    public void XmlDuplicateTagFact_spans_the_whole_element_including_closing_tag()
    {
        // Greying out (Unnecessary) must cover the whole dead element, not just its opening tag.
        //  line 0: <Root>
        //  line 1: <Max_Speed>1.0</Max_Speed>
        //  line 2: <Max_Speed>2.0</Max_Speed>
        const string xml = "<Root>\n<Max_Speed>1.0</Max_Speed>\n<Max_Speed>2.0</Max_Speed>\n</Root>";
        var facts = Build().Produce(xml, Uri);
        var dups = facts.OfType<XmlDuplicateTagFact>().OrderBy(d => d.Line).ToList();

        Assert.Equal(2, dups.Count);
        Assert.Equal(1, dups[0].EndLine);
        Assert.Equal("<Max_Speed>1.0</Max_Speed>".Length, dups[0].EndColumn);
        Assert.Equal(2, dups[1].EndLine);
    }

    [Fact]
    public void XmlDuplicateTagFact_marks_only_the_last_occurrence_as_last()
    {
        // The game keeps the LAST occurrence - the facts must say which one that is so earlier
        // ones can be greyed out.
        const string xml =
            "<Root>\n<Max_Speed>1.0</Max_Speed>\n<Max_Speed>2.0</Max_Speed>\n<Max_Speed>3.0</Max_Speed>\n</Root>";
        var facts = Build().Produce(xml, Uri);
        var dups = facts.OfType<XmlDuplicateTagFact>().OrderBy(d => d.Line).ToList();

        Assert.Equal(3, dups.Count);
        Assert.False(dups[0].IsLastOccurrence);
        Assert.False(dups[1].IsLastOccurrence);
        Assert.True(dups[2].IsLastOccurrence);
    }

    [Fact]
    public void Duplicate_singleton_still_emits_a_value_fact_for_the_occurrence_the_game_keeps()
    {
        // A duplicate tag is a structural complaint about WHERE the value sits; it says nothing
        // about whether the value is valid. Suppressing the value fact meant a duplicated tag also
        // silently escaped every value check - a wrong enum, a bad float, a missing reference - so
        // fixing the duplicate was the only way to discover the second, unrelated error.
        const string xml = "<Root>\n<Max_Speed>1.0</Max_Speed>\n<Max_Speed>2.0</Max_Speed>\n</Root>";
        var facts = Build().Produce(xml, Uri);

        var value = Assert.Single(facts.OfType<XmlTagValueFact>());
        // The LAST one: earlier occurrences are dead weight the engine never reads, so validating
        // them would report errors against text that has no effect on the game.
        Assert.Equal("2.0", value.RawValue);
    }

    [Fact]
    public void Duplicate_singleton_value_fact_points_at_the_value_not_the_tag()
    {
        const string xml = "<Root>\n<Max_Speed>1.0</Max_Speed>\n<Max_Speed>2.0</Max_Speed>\n</Root>";
        var facts = Build().Produce(xml, Uri);

        var value = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal(2, value.Line);
        Assert.Equal("<Max_Speed>".Length, value.Column);
        Assert.Equal("2.0".Length, value.Length);
    }

    [Fact]
    public void MultipleAllowed_tag_appearing_twice_does_not_emit_XmlDuplicateTagFact()
    {
        const string xml = "<Root><Multi_Tag>a</Multi_Tag><Multi_Tag>b</Multi_Tag></Root>";
        var schema = new MultiTagSchemaProvider();
        var facts = Build(schema).Produce(xml, Uri);
        Assert.Empty(facts.OfType<XmlDuplicateTagFact>());
    }

    // ── notes facts ───────────────────────────────────────────────────────────

    [Fact]
    public void Tag_with_notes_emits_XmlNotesFact()
    {
        const string xml = "<Root><Notes_Tag>1.0</Notes_Tag></Root>";
        var schema = new NotesTagSchemaProvider();
        var facts = Build(schema).Produce(xml, Uri);
        Assert.Single(facts.OfType<XmlNotesFact>());
    }

    [Fact]
    public void Tag_without_notes_does_not_emit_XmlNotesFact()
    {
        const string xml = "<Root><Max_Speed>1.0</Max_Speed></Root>";
        var facts = Build().Produce(xml, Uri);
        Assert.Empty(facts.OfType<XmlNotesFact>());
    }

    // ── position ─────────────────────────────────────────────────────────────

    [Fact]
    public void XmlTagValueFact_has_non_negative_line_and_column()
    {
        const string xml = "<Root><Max_Speed>5.0</Max_Speed></Root>";
        var facts = Build().Produce(xml, Uri);
        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.True(tvf.Line >= 0);
        Assert.True(tvf.Column >= 0);
    }

    [Fact]
    public void XmlTagValueFact_InlineValue_PointsToValueStart()
    {
        // <Root><Max_Speed>90.0</Max_Speed></Root>
        //                  ^ col 17 (0-based), length 4
        const string xml = "<Root><Max_Speed>90.0</Max_Speed></Root>";
        var facts = Build().Produce(xml, Uri);
        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal(0, tvf.Line);
        Assert.Equal(17, tvf.Column);
        Assert.Equal(4, tvf.Length);
    }

    [Fact]
    public void XmlTagValueFact_MultiLineValue_PointsToValueLine()
    {
        // Line 0: <Root>
        // Line 1: <Max_Speed>
        // Line 2:     90.0     ← value at col 4, length 4
        // Line 3: </Max_Speed>
        // Line 4: </Root>
        const string xml = "<Root>\n<Max_Speed>\n    90.0\n</Max_Speed>\n</Root>";
        var facts = Build().Produce(xml, Uri);
        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal(2, tvf.Line);
        Assert.Equal(4, tvf.Column);
        Assert.Equal(4, tvf.Length);
    }

    // ── ability sub-object fact production ───────────────────────────────────

    [Fact]
    public void AbilityField_EmitsFactWithAbilityTypeTagDefinition()
    {
        // <Root><Abilities SubObjectList="Yes"><Lucky_Shot_Attack_Ability Name="...">
        //   <Applicable_Unit_Categories>INFANTRY</Applicable_Unit_Categories>
        // </Lucky_Shot_Attack_Ability></Abilities></Root>
        const string xml =
            "<Root><Abilities SubObjectList=\"Yes\">" +
            "<Lucky_Shot_Attack_Ability Name=\"Luke_Shot\">" +
            "<Applicable_Unit_Categories>INFANTRY</Applicable_Unit_Categories>" +
            "</Lucky_Shot_Attack_Ability></Abilities></Root>";

        var facts = Build(new AbilitySubObjectSchemaProvider()).Produce(xml, Uri);

        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal("Applicable_Unit_Categories", tvf.Tag.Tag, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("INFANTRY", tvf.RawValue);
        // Fact must carry the ability type's tag definition (NameReference), not a global Float
        Assert.Equal(XmlValueType.NameReference, tvf.Tag.ValueType);
    }

    [Fact]
    public void AbilityField_UnknownInAbilityType_FallsBackToGlobalTagGracefully()
    {
        const string xml =
            "<Root><Abilities SubObjectList=\"Yes\">" +
            "<Lucky_Shot_Attack_Ability Name=\"Luke_Shot\">" +
            "<Max_Speed>5.0</Max_Speed>" +
            "</Lucky_Shot_Attack_Ability></Abilities></Root>";

        // Max_Speed is not in LuckyShotAttackAbility schema, but IS a global tag (Float)
        var facts = Build(new AbilitySubObjectSchemaProvider()).Produce(xml, Uri);

        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal("Max_Speed", tvf.Tag.Tag, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(XmlValueType.Float, tvf.Tag.ValueType);
    }

    // ── type-container context resolution ────────────────────────────────────

    [Fact]
    public void TypeContainer_SFXEvent_TextId_EmitsNameReferenceListFact()
    {
        // SFXEvent.Text_ID is NameReferenceList; global GetTag("Text_ID") returns NameReference.
        // Without object-type context the wrong ValueType reaches the handler.
        const string uri = "file:///sfx/SFXEvents.xml";
        const string xml =
            "<SFXEventFiles><SFXEvent Name=\"foo\"><Text_ID>K1 K2</Text_ID></SFXEvent></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry()).Produce(xml, uri);
        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal(XmlValueType.NameReferenceList, tvf.Tag.ValueType);
    }

    // ── structural validation ─────────────────────────────────────────────────

    [Fact]
    public void Well_formed_xml_emits_no_XmlStructureFact()
    {
        const string xml = "<Root><Max_Speed>5.0</Max_Speed></Root>";
        var facts = Build().Produce(xml, Uri);
        Assert.Empty(facts.OfType<XmlStructureFact>());
    }

    [Fact]
    public void Mismatched_closing_tag_emits_XmlStructureFact()
    {
        const string xml = "<Foo><Bar></Foo>";
        var facts = Build().Produce(xml, Uri);
        var sf = Assert.Single(facts.OfType<XmlStructureFact>());
        Assert.Equal(Uri, sf.DocumentUri);
        Assert.Contains("Bar", sf.Reason);
    }

    [Fact]
    public void Unclosed_tag_emits_XmlStructureFact()
    {
        const string xml = "<Foo><Bar>";
        var facts = Build().Produce(xml, Uri);
        Assert.NotEmpty(facts.OfType<XmlStructureFact>());
    }

    [Fact]
    public void Malformed_attribute_emits_XmlStructureFact()
    {
        const string xml = "<Foo attr=value />";
        var facts = Build().Produce(xml, Uri);
        Assert.NotEmpty(facts.OfType<XmlStructureFact>());
    }

    [Fact]
    public void XmlStructureFact_carries_nonnegative_line_and_column()
    {
        const string xml = "<Foo>\n  <Bar>\n</Foo>";
        var facts = Build().Produce(xml, Uri);
        var sf = Assert.Single(facts.OfType<XmlStructureFact>());
        Assert.True(sf.Line >= 0);
        Assert.True(sf.Column >= 0);
    }

    // ── cross-tag rule integration ────────────────────────────────────────────

    [Fact]
    public void CrossTagRule_integrated_via_Pass3_emits_SquadronOffsetsMismatchFact()
    {
        const string xml = "<Root><Obj>" +
                           "<Squadron_Units>A, B</Squadron_Units>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "</Obj></Root>";
        var facts = Build(crossTagRules: [new SquadronOffsetsRule()]).Produce(xml, Uri);
        var fact = Assert.Single(facts.OfType<SquadronOffsetsMismatchFact>());
        Assert.Equal(2, fact.TotalUnits);
        Assert.Equal(1, fact.TotalOffsets);
    }

    [Fact]
    public void No_CrossTagRules_registered_does_not_emit_SquadronOffsetsMismatchFact()
    {
        const string xml = "<Root><Obj>" +
                           "<Squadron_Units>A, B</Squadron_Units>" +
                           "</Obj></Root>";
        var facts = Build().Produce(xml, Uri);
        Assert.Empty(facts.OfType<SquadronOffsetsMismatchFact>());
    }

    // ── unnamed objects (issue #122) ──────────────────────────────────────────

    /// <summary>
    ///     An object whose name attribute is empty is dead content: the parser skips it with a
    ///     debug log, so it produces no symbol, cannot be referenced, cannot be overridden, and
    ///     never appears anywhere the author might notice it is gone.
    /// </summary>
    private const string SfxUri = "file:///audio/SFXEventFiles.xml";
    private const string HardPointUri = "file:///units/HardPoints.xml";
    private const string GameObjectUri = "file:///units/SpaceUnits.xml";

    [Fact]
    public void Empty_name_attribute_emits_UnnamedObjectFact()
    {
        const string xml = "<SFXEventFiles><SFXEvent Name=\"\"><Text_ID>A</Text_ID></SFXEvent></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry()).Produce(xml, SfxUri);

        var fact = Assert.Single(facts.OfType<XmlUnnamedObjectFact>());
        Assert.Equal("SFXEvent", fact.TypeName);
        Assert.Equal("Name", fact.NameTag);
    }

    // Trimmed, like the parser does - whitespace is not a name.
    [Fact]
    public void Whitespace_only_name_attribute_emits_UnnamedObjectFact()
    {
        const string xml = "<SFXEventFiles><SFXEvent Name=\"   \"/></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry()).Produce(xml, SfxUri);

        Assert.Single(facts.OfType<XmlUnnamedObjectFact>());
    }

    [Fact]
    public void Missing_name_attribute_emits_UnnamedObjectFact()
    {
        const string xml = "<SFXEventFiles><SFXEvent/></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry()).Produce(xml, SfxUri);

        Assert.Single(facts.OfType<XmlUnnamedObjectFact>());
    }

    [Fact]
    public void Named_object_emits_no_UnnamedObjectFact()
    {
        const string xml = "<SFXEventFiles><SFXEvent Name=\"Fine\"/></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry()).Produce(xml, SfxUri);

        Assert.Empty(facts.OfType<XmlUnnamedObjectFact>());
    }

    // The whole reason 44 vanilla files LOOK like they have empty names: the blocks are commented
    // out. HAP reports those as comment nodes, so nothing must come of them.
    [Fact]
    public void Commented_out_object_emits_no_UnnamedObjectFact()
    {
        const string xml = "<SFXEventFiles><!-- <SFXEvent Name=\"\"/> --></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry()).Produce(xml, SfxUri);

        Assert.Empty(facts.OfType<XmlUnnamedObjectFact>());
    }

    // A document the registry does not type has no object shape to judge, so the rule stays quiet
    // rather than guessing that every root child ought to carry a name.
    [Fact]
    public void Untyped_document_emits_no_UnnamedObjectFact()
    {
        const string xml = "<Root><Obj/></Root>";
        var facts = Build().Produce(xml, Uri);

        Assert.Empty(facts.OfType<XmlUnnamedObjectFact>());
    }

    // ── unknown tag facts ─────────────────────────────────────────────────────

    [Fact]
    public void Unknown_tag_on_an_object_emits_XmlUnknownTagFact()
    {
        const string xml =
            "<SFXEventFiles><SFXEvent Name=\"A\"><Bogus_Tag>1</Bogus_Tag></SFXEvent></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry())
            .Produce(xml, SfxUri);

        var fact = Assert.Single(facts.OfType<XmlUnknownTagFact>());
        Assert.Equal("Bogus_Tag", fact.TagName);
        Assert.Equal("SFXEvent", fact.OwnerElement, StringComparer.OrdinalIgnoreCase);
        Assert.Null(fact.Suggestion);
    }

    // The authored casing, not HAP's lowercased node name - the message quotes the tag back at the
    // reader, and a lowercased quote reads like a second, invented problem.
    [Fact]
    public void XmlUnknownTagFact_carries_the_tag_name_as_authored()
    {
        const string xml =
            "<SFXEventFiles><SFXEvent Name=\"A\"><CamelCased_Tag>1</CamelCased_Tag></SFXEvent></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry())
            .Produce(xml, SfxUri);

        Assert.Equal("CamelCased_Tag", Assert.Single(facts.OfType<XmlUnknownTagFact>()).TagName);
    }

    [Fact]
    public void Known_tag_emits_no_XmlUnknownTagFact()
    {
        const string xml =
            "<SFXEventFiles><SFXEvent Name=\"A\"><Text_ID>X</Text_ID></SFXEvent></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry())
            .Produce(xml, SfxUri);

        Assert.Empty(facts.OfType<XmlUnknownTagFact>());
    }

    /// <summary>
    ///     Only the direct children of an object element are judged.
    /// </summary>
    /// <remarks>
    ///     Deeper elements are the CONTENTS of a tag - the rows of a sub-object list, the entries of
    ///     a curve - and the schema describes those through the tag's value type rather than by
    ///     naming them. Judging them against the tag vocabulary would report every one of them.
    /// </remarks>
    [Fact]
    public void Element_nested_below_a_known_tag_emits_no_XmlUnknownTagFact()
    {
        const string xml =
            "<SFXEventFiles><SFXEvent Name=\"A\"><Text_ID><Row>1</Row></Text_ID></SFXEvent></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry())
            .Produce(xml, SfxUri);

        Assert.Empty(facts.OfType<XmlUnknownTagFact>());
    }

    // Nothing types the document, so there is no vocabulary to judge it against and no fact.
    [Fact]
    public void Untyped_document_emits_no_XmlUnknownTagFact()
    {
        const string xml = "<Root><Obj><Bogus_Tag>1</Bogus_Tag></Obj></Root>";
        var facts = Build().Produce(xml, Uri);

        Assert.Empty(facts.OfType<XmlUnknownTagFact>());
    }

    // The point of the rule: a typo is a near miss of a real tag, so name the tag it nearly is.
    [Fact]
    public void Near_miss_of_a_known_tag_suggests_that_tag()
    {
        const string xml =
            "<SFXEventFiles><SFXEvent Name=\"A\"><Text_I>X</Text_I></SFXEvent></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry())
            .Produce(xml, SfxUri);

        Assert.Equal("Text_ID", Assert.Single(facts.OfType<XmlUnknownTagFact>()).Suggestion);
    }

    /// <summary>
    ///     A grouping element the schema does not model is not a mistyped tag.
    /// </summary>
    /// <remarks>
    ///     <c>Radarmap.xml</c> is the shipped case: <c>&lt;RadarMap&gt;</c> wraps its tags in
    ///     <c>&lt;RadarMapEvents&gt;</c> and <c>&lt;RadarMapSettings&gt;</c>, which the engine reads
    ///     straight through and our <c>RadarMap</c> type therefore flattens away. Without this the
    ///     rule complains about the shape of a file that loads correctly.
    /// </remarks>
    [Fact]
    public void Unknown_element_holding_only_elements_emits_no_XmlUnknownTagFact()
    {
        const string xml =
            "<SFXEventFiles><SFXEvent Name=\"A\"><Grouping><Text_ID>X</Text_ID></Grouping></SFXEvent></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry())
            .Produce(xml, SfxUri);

        Assert.Empty(facts.OfType<XmlUnknownTagFact>());
    }

    // The other side of that line: a mistyped tag still carries the value it was written for.
    [Fact]
    public void Unknown_element_carrying_a_value_still_emits_XmlUnknownTagFact()
    {
        const string xml =
            "<SFXEventFiles><SFXEvent Name=\"A\"><Bogus_Tag>1</Bogus_Tag></SFXEvent></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry())
            .Produce(xml, SfxUri);

        Assert.Equal("Bogus_Tag", Assert.Single(facts.OfType<XmlUnknownTagFact>()).TagName);
    }

    /// <summary>
    ///     Two edits out of a seven-character name is a different tag, not a typo of this one.
    /// </summary>
    /// <remarks>
    ///     The budget is a sixth of the name's length, so short names get one edit and only long
    ///     compound ones get three. Pinned because the first calibration was a flat three, which
    ///     answered <c>Shader_Name</c> with <c>Saber_Name</c> - a wrong suggestion is worse than
    ///     none, since it sends the reader to rename a tag to something never meant.
    /// </remarks>
    [Fact]
    public void Distant_name_suggests_nothing()
    {
        const string xml =
            "<SFXEventFiles><SFXEvent Name=\"A\"><Tixt_XD>1</Tixt_XD></SFXEvent></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry())
            .Produce(xml, SfxUri);

        Assert.Null(Assert.Single(facts.OfType<XmlUnknownTagFact>()).Suggestion);
    }

    // ── variant tag on a type that has none ──────────────────────────────────

    /// <summary>
    ///     <c>Variant_Of_Existing_Type</c> works on <c>GameObjectType</c> and nothing else.
    /// </summary>
    /// <remarks>
    ///     The whole derivation machinery - <c>Overlay_Object_Type</c>, <c>Overlay_Types</c> -
    ///     exists only for that class, and there is no second derivation path in the engine. Every
    ///     other class parses its own tags and warns about what it does not recognise:
    ///     <c>HardPointDataClass::Parse_Database_Entry() - Unprocessed entry
    ///     'Variant_Of_Existing_Type'</c>. The object loads anyway, with the tag ignored.
    /// </remarks>
    [Fact]
    public void Variant_tag_on_a_type_without_variants_emits_a_fact()
    {
        const string xml =
            "<HardPoints><HardPoint Name=\"HP\"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type></HardPoint></HardPoints>";
        var facts = Build(new VariantAwareSchemaProvider(), new HardPointFileTypeRegistry())
            .Produce(xml, HardPointUri);

        var fact = Assert.Single(facts.OfType<VariantTagNotSupportedFact>());
        Assert.Equal("HardPoint", fact.TypeName);
        Assert.Equal("Variant_Of_Existing_Type", fact.TagName);
    }

    // The same tag on the one type that DOES support it is ordinary, correct authoring.
    [Fact]
    public void Variant_tag_on_a_type_with_variants_emits_no_fact()
    {
        const string xml =
            "<GameObjectFiles><SpaceUnit Name=\"U\"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type></SpaceUnit></GameObjectFiles>";
        var facts = Build(new VariantAwareSchemaProvider(), new GameObjectFileTypeRegistry())
            .Produce(xml, GameObjectUri);

        Assert.Empty(facts.OfType<VariantTagNotSupportedFact>());
    }

    // Untyped document: nothing says which type this is, so nothing can say the tag is wrong here.
    [Fact]
    public void Variant_tag_in_an_untyped_document_emits_no_fact()
    {
        const string xml =
            "<Root><Obj><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type></Obj></Root>";
        var facts = Build(new VariantAwareSchemaProvider()).Produce(xml, Uri);

        Assert.Empty(facts.OfType<VariantTagNotSupportedFact>());
    }

    [Fact]
    public void Object_element_itself_emits_no_XmlUnknownTagFact()
    {
        // <SFXEvent> is an object, not a tag. Reporting it would fire on every object in the file.
        const string xml = "<SFXEventFiles><SFXEvent Name=\"A\"/></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry())
            .Produce(xml, SfxUri);

        Assert.Empty(facts.OfType<XmlUnknownTagFact>());
    }
}

// ── fakes ────────────────────────────────────────────────────────────────────

file sealed class SingleTagSchemaProvider : ISchemaProvider
{
    private static readonly XmlTagDefinition MaxSpeed = new()
        { Tag = "Max_Speed", ValueType = XmlValueType.Float, MultipleAllowed = false };

    public XmlTagDefinition? GetTag(string tagName)
    {
        return tagName.Equals("Max_Speed", StringComparison.OrdinalIgnoreCase) ? MaxSpeed : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [MaxSpeed];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class MultiTagSchemaProvider : ISchemaProvider
{
    private static readonly XmlTagDefinition MultiTag = new()
        { Tag = "Multi_Tag", ValueType = XmlValueType.Float, MultipleAllowed = true };

    public XmlTagDefinition? GetTag(string tagName)
    {
        return tagName.Equals("Multi_Tag", StringComparison.OrdinalIgnoreCase) ? MultiTag : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [MultiTag];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class NotesTagSchemaProvider : ISchemaProvider
{
    private static readonly XmlTagDefinition NotesTag = new()
    {
        Tag = "Notes_Tag", ValueType = XmlValueType.Float, MultipleAllowed = false,
        Notes = new Dictionary<string, string> { ["en"] = "A tag with notes" }
    };

    public XmlTagDefinition? GetTag(string tagName)
    {
        return tagName.Equals("Notes_Tag", StringComparison.OrdinalIgnoreCase) ? NotesTag : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [NotesTag];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class EmptySchemaProvider : ISchemaProvider
{
    public XmlTagDefinition? GetTag(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class AbilitySubObjectSchemaProvider : ISchemaProvider
{
    private static readonly XmlTagDefinition AbilitiesTag = new()
        { Tag = "Abilities", ValueType = XmlValueType.AbilityDefinitionSubObjectList };

    private static readonly XmlTagDefinition ApplicableUnitCategoriesTag = new()
        { Tag = "Applicable_Unit_Categories", ValueType = XmlValueType.NameReference };

    private static readonly XmlTagDefinition MaxSpeedTag = new()
        { Tag = "Max_Speed", ValueType = XmlValueType.Float };

    public XmlTagDefinition? GetTag(string tagName)
    {
        if (tagName.Equals("Abilities", StringComparison.OrdinalIgnoreCase)) return AbilitiesTag;
        if (tagName.Equals("Max_Speed", StringComparison.OrdinalIgnoreCase)) return MaxSpeedTag;
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        if (typeName.Equals("LuckyShotAttackAbility", StringComparison.OrdinalIgnoreCase))
            return [ApplicableUnitCategoriesTag];
        return [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [AbilitiesTag, ApplicableUnitCategoriesTag, MaxSpeedTag];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class SfxEventSchemaProvider : ISchemaProvider
{
    private static readonly XmlTagDefinition GlobalTextId = new()
        { Tag = "Text_ID", ValueType = XmlValueType.NameReference };

    private static readonly XmlTagDefinition SfxTextId = new()
        { Tag = "Text_ID", ValueType = XmlValueType.NameReferenceList };

    private static readonly GameObjectTypeDefinition SfxEventTypeDef = new()
        { TypeName = "SFXEvent", NameTag = "Name" };

    public XmlTagDefinition? GetTag(string tagName)
    {
        return tagName.Equals("Text_ID", StringComparison.OrdinalIgnoreCase) ? GlobalTextId : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string typeName)
    {
        return typeName.Equals("SFXEvent", StringComparison.OrdinalIgnoreCase) ? SfxEventTypeDef : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        return typeName.Equals("SFXEvent", StringComparison.OrdinalIgnoreCase) ? [SfxTextId] : [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [GlobalTextId, SfxTextId];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [SfxEventTypeDef];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

/// <summary>
///     Two object types, of which only <c>GameObjectType</c> declares the variant tag - which is
///     exactly the shape of the real schema, where <c>Variant_Of_Existing_Type</c> appears in
///     <c>GameObjectType.yaml</c> and nowhere else.
/// </summary>
file sealed class VariantAwareSchemaProvider : ISchemaProvider
{
    private static readonly XmlTagDefinition VariantTag = new()
    {
        Tag = "Variant_Of_Existing_Type", ValueType = XmlValueType.TypeReference,
        SemanticType = TagSemanticType.VariantParent
    };

    private static readonly XmlTagDefinition BoneTag = new()
        { Tag = "Attachment_Bone", ValueType = XmlValueType.NameReference };

    private static readonly GameObjectTypeDefinition HardPointType = new()
        { TypeName = "HardPoint", NameTag = "Name" };

    private static readonly GameObjectTypeDefinition GameObjectType = new()
        { TypeName = "GameObjectType", NameTag = "Name" };

    public XmlTagDefinition? GetTag(string tagName)
    {
        if (tagName.Equals("Variant_Of_Existing_Type", StringComparison.OrdinalIgnoreCase))
            return VariantTag;
        return tagName.Equals("Attachment_Bone", StringComparison.OrdinalIgnoreCase) ? BoneTag : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string typeName)
    {
        if (typeName.Equals("HardPoint", StringComparison.OrdinalIgnoreCase)) return HardPointType;
        return typeName.Equals("GameObjectType", StringComparison.OrdinalIgnoreCase)
            ? GameObjectType
            : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        if (typeName.Equals("GameObjectType", StringComparison.OrdinalIgnoreCase))
            return [VariantTag, BoneTag];
        return typeName.Equals("HardPoint", StringComparison.OrdinalIgnoreCase) ? [BoneTag] : [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [VariantTag, BoneTag];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [HardPointType, GameObjectType];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class HardPointFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray.Create("HardPoint");
    }

    public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string normalizedPath)
    {
    }
}

file sealed class GameObjectFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray.Create("GameObjectType");
    }

    public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string normalizedPath)
    {
    }
}

file sealed class SfxEventFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray.Create("SFXEvent");
    }

    public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string normalizedPath)
    {
    }
}

file sealed class EmptyFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All => new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string normalizedPath)
    {
    }
}