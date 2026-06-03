// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class NameReferenceHandlerTest
{
    private static readonly NameReferenceHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Alt_Name", XmlValueType.NameReference);

    [Theory]
    [InlineData("SomeName")]
    [InlineData("Bone_Root")]
    public void Non_empty_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Wrong_type_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, ""), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    // ── HardcodedSet validation ──────────────────────────────────────────────

    private static XmlTagDefinition HardcodedTag(HardcodedReferenceSet set)
    {
        return new XmlTagDefinition
        {
            Tag = "Type",
            ValueType = XmlValueType.NameReference,
            ReferenceKind = ReferenceKind.HardcodedSet,
            HardcodedSet = set
        };
    }

    private static HardcodedReferenceSet AbilityTypeSet(params string[] names)
    {
        return new HardcodedReferenceSet
        {
            Name = "AbilityType",
            Values = names.Select(n => new HardcodedReferenceSetValue { Name = n }).ToList()
        };
    }

    [Theory]
    [InlineData("HUNT")]
    [InlineData("hunt")]
    [InlineData("Force_Cloak")]
    public void HardcodedSet_KnownValue_ReturnsNoDiagnostics(string value)
    {
        var tag = HardcodedTag(AbilityTypeSet("HUNT", "Force_Cloak", "DEFEND"));
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void HardcodedSet_UnknownValue_ReturnsError()
    {
        var tag = HardcodedTag(AbilityTypeSet("HUNT", "DEFEND"));
        var results = Sut
            .Handle(XmlHandlerTestFixtures.MakeFact(tag, "UNKNOWN_ABILITY"), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void HardcodedSet_NoSet_ReturnsNoDiagnostics()
    {
        var tag = new XmlTagDefinition
        {
            Tag = "Type",
            ValueType = XmlValueType.NameReference,
            ReferenceKind = ReferenceKind.HardcodedSet,
            HardcodedSet = null
        };
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "ANYTHING"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }
}