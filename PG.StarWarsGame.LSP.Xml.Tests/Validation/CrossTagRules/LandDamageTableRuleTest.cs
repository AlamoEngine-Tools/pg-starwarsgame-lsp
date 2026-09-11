// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.CrossTagRules;

/// <summary>
///     The <c>Land_Damage_*</c> trio (issue #102): a positional table whose columns must line up.
/// </summary>
/// <remarks>
///     <para>
///         The issue asks that all THREE columns carry the same number of entries. Measured with XML
///         comments stripped, that rule fires on the base game: <c>Land_Damage_SFX</c> disagrees with
///         <c>Land_Damage_Alternates</c> on 42 of foc's 219 objects and 37 of eaw's 161, in two
///         shapes - 35 objects pair one alternate with four <c>null</c> sounds, and 7 pair three
///         alternates with four entries of which the middle two are real. So only the
///         threshold/alternate pairing is enforced here; it disagrees in ZERO of either corpus.
///     </para>
///     <para>
///         Silent when either column is absent. The rule reads the document node, not the effective
///         object, so a variant that overrides one column and inherits the other looks single-column
///         from here - and "cannot tell" is not "fails". Nothing in either corpus declares one of
///         the two alone, so the case costs no coverage.
///     </para>
/// </remarks>
public sealed class LandDamageTableRuleTest
{
    private const string Uri = "file:///units/Groundstructures.xml";

    [Fact]
    public void Columns_of_equal_length_emit_no_fact()
    {
        const string xml = "<Root><Obj>" +
                           "<Land_Damage_Thresholds>1, 0.66, 0.33</Land_Damage_Thresholds>" +
                           "<Land_Damage_Alternates>0, 1, 2</Land_Damage_Alternates>" +
                           "</Obj></Root>";
        Assert.Empty(Facts(xml));
    }

    [Fact]
    public void More_thresholds_than_alternates_emits_a_fact()
    {
        const string xml = "<Root><Obj>" +
                           "<Land_Damage_Thresholds>1, 0.66, 0.33, 0</Land_Damage_Thresholds>" +
                           "<Land_Damage_Alternates>0, 1, 2</Land_Damage_Alternates>" +
                           "</Obj></Root>";
        var fact = Assert.Single(Facts(xml));
        Assert.Equal(4, fact.Thresholds);
        Assert.Equal(3, fact.Alternates);
        Assert.Equal(Uri, fact.DocumentUri);
    }

    [Fact]
    public void More_alternates_than_thresholds_emits_a_fact()
    {
        const string xml = "<Root><Obj>" +
                           "<Land_Damage_Thresholds>1, 0.5</Land_Damage_Thresholds>" +
                           "<Land_Damage_Alternates>0, 1, 2</Land_Damage_Alternates>" +
                           "</Obj></Root>";
        var fact = Assert.Single(Facts(xml));
        Assert.Equal(2, fact.Thresholds);
        Assert.Equal(3, fact.Alternates);
    }

    // The whole reason this rule reports two columns rather than three.
    [Fact]
    public void A_disagreeing_SFX_column_is_never_reported()
    {
        const string xml = "<Root><Obj>" +
                           "<Land_Damage_Thresholds>1</Land_Damage_Thresholds>" +
                           "<Land_Damage_Alternates>0</Land_Damage_Alternates>" +
                           "<Land_Damage_SFX>null, null, null, null</Land_Damage_SFX>" +
                           "</Obj></Root>";
        Assert.Empty(Facts(xml));
    }

    [Theory]
    [InlineData("<Land_Damage_Thresholds>1, 0.5</Land_Damage_Thresholds>")]
    [InlineData("<Land_Damage_Alternates>0, 1</Land_Damage_Alternates>")]
    public void One_column_alone_emits_no_fact(string only)
    {
        Assert.Empty(Facts("<Root><Obj>" + only + "</Obj></Root>"));
    }

    [Fact]
    public void An_object_with_no_damage_table_emits_no_fact()
    {
        Assert.Empty(Facts("<Root><Obj><Tactical_Health>500</Tactical_Health></Obj></Root>"));
    }

    // Trailing and doubled separators are written by hand often enough to matter, and an empty
    // slot is not an entry: this table pairs by position and there is nothing at that position.
    [Fact]
    public void Empty_entries_are_not_counted()
    {
        const string xml = "<Root><Obj>" +
                           "<Land_Damage_Thresholds>1, 0.66, 0.33,</Land_Damage_Thresholds>" +
                           "<Land_Damage_Alternates>0, 1, 2</Land_Damage_Alternates>" +
                           "</Obj></Root>";
        Assert.Empty(Facts(xml));
    }

    [Fact]
    public void The_fact_carries_both_column_positions()
    {
        const string xml = "<Root><Obj>\n" +
                           "<Land_Damage_Thresholds>1, 0.5</Land_Damage_Thresholds>\n" +
                           "<Land_Damage_Alternates>0, 1, 2</Land_Damage_Alternates>\n" +
                           "</Obj></Root>";
        var fact = Assert.Single(Facts(xml));

        Assert.Equal(1, fact.ThresholdsPosition.Line);
        Assert.Equal(2, fact.AlternatesPosition.Line);
        Assert.True(fact.ThresholdsPosition.Length > 0);
        Assert.True(fact.AlternatesPosition.Length > 0);
    }

    [Fact]
    public void Each_mismatched_object_emits_its_own_fact()
    {
        const string xml = "<Root>" +
                           "<Obj1><Land_Damage_Thresholds>1</Land_Damage_Thresholds>" +
                           "<Land_Damage_Alternates>0, 1</Land_Damage_Alternates></Obj1>" +
                           "<Obj2><Land_Damage_Thresholds>1, 0.5</Land_Damage_Thresholds>" +
                           "<Land_Damage_Alternates>0, 1, 2</Land_Damage_Alternates></Obj2>" +
                           "</Root>";
        Assert.Equal(2, Facts(xml).Count());
    }

    [Fact]
    public void No_rules_registered_emits_no_facts()
    {
        const string xml = "<Root><Obj>" +
                           "<Land_Damage_Thresholds>1</Land_Damage_Thresholds>" +
                           "<Land_Damage_Alternates>0, 1</Land_Damage_Alternates>" +
                           "</Obj></Root>";
        Assert.Empty(BuildProducer().Produce(xml, Uri).OfType<LandDamageTableMismatchFact>());
    }

    private static IEnumerable<LandDamageTableMismatchFact> Facts(string xml)
    {
        return BuildProducer(new LandDamageTableRule())
            .Produce(xml, Uri)
            .OfType<LandDamageTableMismatchFact>();
    }

    private static XmlDocumentFactProducer BuildProducer(IXmlCrossTagRule? rule = null)
    {
        return new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            rule is null ? [] : [rule]);
    }
}

file sealed class EmptyFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string fileUri)
    {
    }
}
