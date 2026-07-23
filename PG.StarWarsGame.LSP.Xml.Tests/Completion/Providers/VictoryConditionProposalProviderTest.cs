// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Completion.Providers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion.Providers;

public sealed class VictoryConditionProposalProviderTest
{
    private static readonly VictoryConditionProposalProvider Sut = new();

    private static EnumDefinition Enum => new()
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

    private static XmlTagDefinition Tag => new()
        { Tag = "Good_Victory_Conditions", ValueType = XmlValueType.Type69, Enum = Enum };

    [Fact]
    public void ValueType_is_Type69()
    {
        Assert.Equal(XmlValueType.Type69, Sut.ValueType);
    }

    [Fact]
    public void Empty_partial_returns_all_values()
    {
        Assert.Equal(3, Sut.GetProposals(Tag, "").Count);
    }

    [Fact]
    public void Partial_filters_by_prefix_case_insensitively()
    {
        var p = Assert.Single(Sut.GetProposals(Tag, "galactic_k"));
        Assert.Equal("Galactic_Kill_Enemy_Leader", p.Label);
    }

    [Fact]
    public void ListCompletion_ExcludesAlreadyListed_AndCompletesTrailingSegment()
    {
        var p = Assert.Single(Sut.GetProposals(Tag, "Galactic_All_Planets_Controlled, Galactic_C"));
        Assert.Equal("Galactic_Cycles_Elapsed", p.Label);
    }

    [Fact]
    public void NoEnum_ReturnsEmpty()
    {
        Assert.Empty(Sut.GetProposals(new XmlTagDefinition { Tag = "X", ValueType = XmlValueType.Type69 }, ""));
    }
}
