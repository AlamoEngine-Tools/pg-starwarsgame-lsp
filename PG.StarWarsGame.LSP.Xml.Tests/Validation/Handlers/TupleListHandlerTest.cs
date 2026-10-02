// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     The default <c>TupleList</c> check: a whole number of groups, and nothing about what the
///     items are.
/// </summary>
/// <remarks>
///     It used to demand a music event name and a positive weight, because the type was read as
///     "a weighted list of music events". The type is the engine's generic pair list; what each
///     item is belongs to the tag's slots, which are checked per item elsewhere.
/// </remarks>
public sealed class TupleListHandlerTest
{
    private static readonly TupleListHandler Sut = new();

    private static readonly XmlTagDefinition Untyped =
        XmlHandlerTestFixtures.MakeTag("Pairs", XmlValueType.TupleList);

    private static readonly XmlTagDefinition TerrainModel = Untyped with
    {
        Slots =
        [
            new TupleSlotDefinition { Label = "Terrain" },
            new TupleSlotDefinition { Label = "Model" }
        ]
    };

    private static List<XmlDiagnosticResult> Check(XmlTagDefinition tag, string value)
    {
        return Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
    }

    [Theory]
    [InlineData("Temperate, EI_TROOPER.ALO")]
    [InlineData("Temperate, EI_TROOPER.ALO, Urban, EI_TROOPER.ALO")]
    [InlineData("Temperate, EI_TROOPER.ALO,")]
    [InlineData("Music_Battle_01, 3")]
    public void AWholeNumberOfPairs_IsFine(string value)
    {
        Assert.Empty(Check(TerrainModel, value));
    }

    [Theory]
    [InlineData("Temperate")]
    [InlineData("Temperate, EI_TROOPER.ALO, Urban")]
    public void AHalfPair_IsReported_InTheSlotsWords(string value)
    {
        var d = Assert.Single(Check(TerrainModel, value));

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("`Terrain, Model` pairs", d.Message);
        Assert.DoesNotContain("music", d.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ATagWithoutSlots_IsReadAsPairs()
    {
        Assert.Empty(Check(Untyped, "a, b, c, d"));
        Assert.Contains("comma-separated pairs", Assert.Single(Check(Untyped, "a, b, c")).Message);
    }

    [Fact]
    public void AGroupOfThree_CountsInThrees()
    {
        var three = TerrainModel with
        {
            Slots = [.. TerrainModel.Slots, new TupleSlotDefinition { Label = "Weight" }]
        };

        Assert.Empty(Check(three, "a, b, c, d, e, f"));
        Assert.Single(Check(three, "a, b, c, d"));
    }

    [Fact]
    public void Wrong_type_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, ""), XmlHandlerTestFixtures.EmptyCtx));
    }
}
