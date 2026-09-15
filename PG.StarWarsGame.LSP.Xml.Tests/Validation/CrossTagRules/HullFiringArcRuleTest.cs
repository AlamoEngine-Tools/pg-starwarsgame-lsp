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
///     A5: the rule notices an object writing a turret-extent tag and names it; whether the arc binds the
///     hull is the handler's call, on the effective object.
/// </summary>
/// <remarks>
///     Anchored on the rotate extent when written, else the elevate extent, else the deployed pair - the
///     first tag a reader of the hint would look for.
/// </remarks>
public sealed class HullFiringArcRuleTest
{
    private const string Uri = "file:///xml/Spaceunitsfighters.xml";

    private static IReadOnlyList<HullFiringArcFact> Produce(string xml)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            [new HullFiringArcRule()]);
        return producer.Produce(xml, Uri).OfType<HullFiringArcFact>().ToList();
    }

    [Fact]
    public void An_object_writing_a_rotate_extent_is_anchored_on_it()
    {
        const string xml =
            "<SpaceUnits>\n" +
            "<SpaceUnit Name=\"X-Wing\">\n" +
            "    <Turret_Elevate_Extent_Degrees>40.0</Turret_Elevate_Extent_Degrees>\n" +
            "    <Turret_Rotate_Extent_Degrees>20.0</Turret_Rotate_Extent_Degrees>\n" +
            "</SpaceUnit>\n" +
            "</SpaceUnits>";

        var fact = Assert.Single(Produce(xml));

        Assert.Equal("X-Wing", fact.ObjectId);
        Assert.Equal(3, fact.Line);
        Assert.Equal(34, fact.Column);
        Assert.Equal(4, fact.Length);
    }

    [Fact]
    public void Only_an_elevate_extent_is_enough()
    {
        const string xml =
            "<SpaceUnits>\n" +
            "<SpaceUnit Name=\"A\">\n" +
            "    <Turret_Elevate_Extent_Degrees>40.0</Turret_Elevate_Extent_Degrees>\n" +
            "</SpaceUnit>\n" +
            "</SpaceUnits>";

        Assert.Equal(2, Assert.Single(Produce(xml)).Line);
    }

    [Fact]
    public void An_object_writing_no_extent_emits_nothing()
    {
        Assert.Empty(Produce("<X><SpaceUnit Name=\"A\"><Max_Speed>2</Max_Speed></SpaceUnit></X>"));
    }

    [Fact]
    public void An_unnamed_object_emits_nothing()
    {
        Assert.Empty(
            Produce("<X><SpaceUnit><Turret_Rotate_Extent_Degrees>20</Turret_Rotate_Extent_Degrees></SpaceUnit></X>"));
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