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
///     The rule only notices a <c>Fires_Forward</c> that is ON and names the object it sits on; what
///     that costs is decided by the handler, against the EFFECTIVE object.
/// </summary>
/// <remarks>
///     The default is false (<c>GameObjectTypeByteMembersClass</c> ctor), so a written <c>No</c> changes
///     nothing and is not this rule's business.
/// </remarks>
public sealed class FiresForwardRuleTest
{
    private const string Uri = "file:///xml/Landbombingrununits.xml";

    private static IReadOnlyList<FiresForwardFact> Produce(string xml)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            [new FiresForwardRule()]);
        return producer.Produce(xml, Uri).OfType<FiresForwardFact>().ToList();
    }

    [Fact]
    public void Fires_forward_on_names_the_object_and_anchors_on_the_value()
    {
        const string xml =
            "<LandBombingUnits>\n" +
            "<LandBombingUnit Name=\"Y-Wing_Bombing_Run\">\n" +
            "    <Fires_Forward>Yes</Fires_Forward>\n" +
            "</LandBombingUnit>\n" +
            "</LandBombingUnits>";

        var fact = Assert.Single(Produce(xml));

        Assert.Equal("Y-Wing_Bombing_Run", fact.ObjectId);
        Assert.Equal(2, fact.Line);
        Assert.Equal(19, fact.Column);
        Assert.Equal(3, fact.Length);
    }

    [Theory]
    [InlineData("No")]
    [InlineData("false")]
    public void Fires_forward_off_emits_nothing(string value)
    {
        var xml = $"<X><SpaceUnit Name=\"A\"><Fires_Forward>{value}</Fires_Forward></SpaceUnit></X>";

        Assert.Empty(Produce(xml));
    }

    [Fact]
    public void No_fires_forward_emits_nothing()
    {
        Assert.Empty(Produce("<X><SpaceUnit Name=\"A\"><Max_Speed>2</Max_Speed></SpaceUnit></X>"));
    }

    // The engine keeps the last occurrence of a repeated singleton tag.
    [Fact]
    public void The_last_occurrence_decides()
    {
        Assert.Single(Produce(
            "<X><SpaceUnit Name=\"A\"><Fires_Forward>No</Fires_Forward><Fires_Forward>Yes</Fires_Forward></SpaceUnit></X>"));
        Assert.Empty(Produce(
            "<X><SpaceUnit Name=\"A\"><Fires_Forward>Yes</Fires_Forward><Fires_Forward>No</Fires_Forward></SpaceUnit></X>"));
    }

    [Fact]
    public void An_unnamed_object_emits_nothing()
    {
        Assert.Empty(Produce("<X><SpaceUnit><Fires_Forward>Yes</Fires_Forward></SpaceUnit></X>"));
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