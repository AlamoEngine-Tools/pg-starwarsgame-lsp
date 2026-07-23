// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class VictoryConditionListHandlerTest
{
    private static readonly VictoryConditionListHandler Sut = new();

    private static EnumDefinition Enum()
    {
        return new EnumDefinition
        {
            Name = "GalacticVictoryCondition",
            Kind = EnumKind.SchemaFixed,
            Values =
            [
                new EnumValueDefinition { Name = "Galactic_All_Planets_Controlled" },
                new EnumValueDefinition { Name = "Galactic_Kill_Enemy_Leader" },
                new EnumValueDefinition { Name = "Galactic_Cycles_Elapsed" }
            ]
        };
    }

    private static XmlTagValueFact Fact(string value)
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Good_Victory_Conditions", XmlValueType.Type69, enumDef: Enum());
        return XmlHandlerTestFixtures.MakeFact(tag, value);
    }

    [Fact]
    public void ValidSingleValue_NoDiagnostic()
    {
        Assert.Empty(Sut.Handle(Fact("Galactic_Kill_Enemy_Leader"), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void ValidMultilineCommaList_NoDiagnostic()
    {
        // Vanilla writes one condition per line, comma-separated - trimming must survive the newlines.
        const string value = "\n\t\tGalactic_All_Planets_Controlled,\n\t\tGalactic_Cycles_Elapsed\n\t";
        Assert.Empty(Sut.Handle(Fact(value), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void UnknownValueInList_EmitsErrorNamingTheToken()
    {
        var results = Sut.Handle(
            Fact("Galactic_All_Planets_Controlled, Made_Up_Condition"), XmlHandlerTestFixtures.EmptyCtx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Made_Up_Condition", d.Message);
    }

    [Fact]
    public void EmptyValue_NoDiagnostic()
    {
        Assert.Empty(Sut.Handle(Fact("   "), XmlHandlerTestFixtures.EmptyCtx));
    }
}
