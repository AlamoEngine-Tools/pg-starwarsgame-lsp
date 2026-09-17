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

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.CrossTagRules;

/// <summary>
///     #104: the rule notices an object writing <c>Specific_Death_Anim_Type</c> and names it. Whether the
///     model has the clip is the handler's call, on the effective object - the model is usually inherited.
/// </summary>
public sealed class DeathAnimationClipRuleTest
{
    private const string Uri = "file:///xml/Groundindigenous.xml";

    private static IReadOnlyList<DeathAnimationClipFact> Produce(string xml)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            [new DeathAnimationClipRule()]);
        return producer.Produce(xml, Uri).OfType<DeathAnimationClipFact>().ToList();
    }

    [Fact]
    public void A_death_animation_type_is_anchored_on_its_value()
    {
        const string xml =
            "<Units>\n" +
            "<Indigenous_Unit Name=\"Mandalorian_Indigenous_Crush_Death_Clone\">\n" +
            "    <Variant_Of_Existing_Type>Mandalorian_Indigenous</Variant_Of_Existing_Type>\n" +
            "    <Specific_Death_Anim_Type>Crushed</Specific_Death_Anim_Type>\n" +
            "</Indigenous_Unit>\n" +
            "</Units>";

        var fact = Assert.Single(Produce(xml));

        Assert.Equal("Mandalorian_Indigenous_Crush_Death_Clone", fact.ObjectId);
        Assert.Equal(3, fact.Line);
        Assert.Equal(30, fact.Column);
        Assert.Equal(7, fact.Length);
    }

    [Fact]
    public void An_empty_type_emits_nothing()
    {
        Assert.Empty(Produce("<X><Unit Name=\"A\"><Specific_Death_Anim_Type /></Unit></X>"));
    }

    [Fact]
    public void An_object_writing_no_type_emits_nothing()
    {
        Assert.Empty(
            Produce("<X><Unit Name=\"A\"><Specific_Death_Anim_Index>1</Specific_Death_Anim_Index></Unit></X>"));
    }

    [Fact]
    public void An_unnamed_object_emits_nothing()
    {
        Assert.Empty(Produce("<X><Unit><Specific_Death_Anim_Type>DIE</Specific_Death_Anim_Type></Unit></X>"));
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