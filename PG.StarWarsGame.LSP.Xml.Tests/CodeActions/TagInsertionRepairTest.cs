// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeActions;

/// <summary>
///     Writing in a tag the engine defaults for you.
/// </summary>
/// <remarks>
///     <para>
///         <c>DemolitionAbilityClass::Validate_Data</c> defaults a missing <c>Bomb_Type</c> to
///         <c>Demolition_Bomb</c>, so the file can be made to say what the game will do with it.
///     </para>
///     <para>
///         The formatting is DERIVED from the document, never chosen: the indentation is whatever
///         the object's existing children use and the line ending is whatever the file already has.
///         HAP cannot be asked to write this - it lower-cases element names, so re-serializing would
///         emit <c>&lt;bomb_type&gt;</c> and reformat the author's file - and there is no document
///         formatter to tidy up afterwards, since a workspace edit does not trigger on-type
///         formatting.
///     </para>
/// </remarks>
public sealed class TagInsertionRepairTest
{
    private const string Uri = "file:///abilities/Abilities.xml";
    private const string Inserted = "<Bomb_Type>Demolition_Bomb</Bomb_Type>";

    [Fact]
    public void The_tag_is_inserted_after_the_last_child_at_its_indentation()
    {
        const string body = "<Demolition_Ability Name='D'>\n"
                            + "        <Damage_Percentage>0.5</Damage_Percentage>\n"
                            + "    </Demolition_Ability>";

        var edit = Single(body);

        Assert.Equal(0, edit.Length); // an insertion replaces nothing
        Assert.Equal("\n        " + Inserted, edit.NewText);
    }

    /// <summary>Whatever the file indents with is what the new line indents with.</summary>
    [Fact]
    public void Tabs_are_preserved()
    {
        const string body = "<Demolition_Ability Name='D'>\n"
                            + "\t\t<Damage_Percentage>0.5</Damage_Percentage>\n"
                            + "\t</Demolition_Ability>";

        Assert.Equal("\n\t\t" + Inserted, Single(body).NewText);
    }

    [Fact]
    public void Windows_line_endings_are_preserved()
    {
        const string body = "<Demolition_Ability Name='D'>\r\n"
                            + "    <Damage_Percentage>0.5</Damage_Percentage>\r\n"
                            + "</Demolition_Ability>";

        Assert.Equal("\r\n    " + Inserted, Single(body).NewText);
    }

    /// <summary>A one-line object stays on one line - a newline there would be a reformat.</summary>
    [Fact]
    public void A_single_line_object_is_extended_inline()
    {
        const string body =
            "<Demolition_Ability Name='D'><Damage_Percentage>0.5</Damage_Percentage></Demolition_Ability>";

        Assert.Equal(Inserted, Single(body).NewText);
    }

    /// <summary>
    ///     With no children there is no indentation to copy, and inventing one is the policy
    ///     decision this design avoids. The diagnostic still stands; it just carries no fix.
    /// </summary>
    [Fact]
    public void An_empty_object_gets_the_diagnostic_but_no_fix()
    {
        var fact = Fact("<Demolition_Ability Name='D'></Demolition_Ability>");

        Assert.Equal("Bomb_Type", fact.TagName);
        Assert.Null(fact.Insertion);
    }

    /// <summary>
    ///     The engine has no default for <c>Attack_Animation</c> - it only complains - so there is
    ///     nothing to write in.
    /// </summary>
    [Fact]
    public void A_tag_the_engine_does_not_default_offers_no_fix()
    {
        var facts = Produce("<Eat_Attack_Ability Name='A'><Damage_Amount>1</Damage_Amount>"
                            + "</Eat_Attack_Ability>", new EatAttackAnimationRule());

        Assert.Null(Assert.Single(facts.OfType<MissingRequiredTagFact>()).Insertion);
    }

    /// <summary>The handler passes the insertion through as the engine-repair quick fix.</summary>
    [Fact]
    public void The_handler_offers_it_as_an_engine_repair()
    {
        const string body = "<Demolition_Ability Name='D'>\n"
                            + "    <Damage_Percentage>0.5</Damage_Percentage>\n"
                            + "</Demolition_Ability>";

        var d = Assert.Single(new MissingRequiredTagHandler()
            .Handle(Fact(body), XmlHandlerTestFixtures.EmptyCtx));

        Assert.NotNull(d.EngineRepair);
        Assert.Contains("Demolition_Bomb", Assert.Single(d.EngineRepair!.Edits).NewText,
            StringComparison.Ordinal);
    }

    private static XmlDiagnosticEdit Single(string body)
    {
        var insertion = Fact(body).Insertion;
        Assert.NotNull(insertion);
        return Assert.Single(insertion!.Edits);
    }

    private static MissingRequiredTagFact Fact(string body)
    {
        return Assert.Single(Produce(body, new DemolitionBombTypeRule())
            .OfType<MissingRequiredTagFact>());
    }

    private static IReadOnlyList<XmlFact> Produce(string body, IXmlCrossTagRule rule)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            [rule]);

        return producer.Produce("<Root>\n" + body + "\n</Root>", Uri);
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
