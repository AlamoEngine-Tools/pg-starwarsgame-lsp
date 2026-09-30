// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Encyclopedia;

namespace PG.StarWarsGame.LSP.Server.Tests.Encyclopedia;

/// <summary>
///     Where the popup's header pieces sit, which the game reads from <c>GameConstants</c> rather
///     than deciding for itself.
/// </summary>
/// <remarks>
///     The card used to hardcode every one of these. They are DATA - a mod can move the blip, the
///     portrait or the cost by editing GameConstants, and the preview would have gone on drawing
///     them where the base game puts them.
/// </remarks>
public sealed class EncyclopediaOffsetsTest
{
    private static GameSymbol Constants()
    {
        return new GameSymbol("GameConstants", GameSymbolKind.XmlObject, "GameConstants",
            new FileOrigin("file:///GameConstants.xml", 0, 0), null);
    }

    private static EncyclopediaLayout Resolve(FakeVariantTagSource source)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("GameConstants", [Constants()])
        };
        return new EncyclopediaLayoutResolver(
            new EffectiveObjectResolver(index, new NullSchemaProvider(), source)).Resolve();
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    /// <summary>The values Empire at War ships, and EaWX keeps.</summary>
    [Fact]
    public void NothingIndexed_UsesTheShippedOffsets()
    {
        var offsets = Resolve(new FakeVariantTagSource()).Offsets;

        Assert.Equal(11, offsets.Population);
        Assert.Equal(68, offsets.Name);
        Assert.Equal(258, offsets.Cost);
        Assert.Equal(39, offsets.IconX);
        Assert.Equal(-12, offsets.IconY);
        Assert.Equal(5, offsets.ClassY);
    }

    [Fact]
    public void AModMovingThePieces_IsFollowed()
    {
        var source = new FakeVariantTagSource().With("GameConstants",
            Tag("Encyclopedia_Population_Offset", "20"),
            Tag("Encyclopedia_Name_Offset", "90"),
            Tag("Encyclopedia_Cost_Offset", "300"),
            Tag("Encyclopedia_Icon_X_Offset", "44"),
            Tag("Encyclopedia_Icon_Y_Offset", "-8"),
            Tag("Encyclopedia_Class_Y_Offset", "7"));

        var offsets = Resolve(source).Offsets;

        Assert.Equal(20, offsets.Population);
        Assert.Equal(90, offsets.Name);
        Assert.Equal(300, offsets.Cost);
        Assert.Equal(44, offsets.IconX);
        Assert.Equal(-8, offsets.IconY);
        Assert.Equal(7, offsets.ClassY);
    }

    /// <summary>A junk value keeps the shipped one rather than collapsing a position to zero.</summary>
    [Fact]
    public void AnUnparsableOffset_KeepsTheShippedValue()
    {
        var source = new FakeVariantTagSource().With("GameConstants",
            Tag("Encyclopedia_Name_Offset", "over there"));

        Assert.Equal(68, Resolve(source).Offsets.Name);
    }

    // ── what the offsets mean, per row ───────────────────────────────────────

    /// <summary>
    ///     With no population there is no blip, and everything that sat to its right moves left by
    ///     exactly the blip's own offset - the engine does this rather than leaving a hole.
    /// </summary>
    [Fact]
    public void WithNoBlip_TheNameAndPortraitShiftLeft()
    {
        var o = EncyclopediaOffsets.Shipped;

        Assert.Equal(68 - 11, o.NameFor(hasBlip: false, hasIcon: true));
        Assert.Equal(39 - 11, o.IconXFor(hasBlip: false));
    }

    [Fact]
    public void WithABlip_TheyStayWhereTheConstantsPutThem()
    {
        var o = EncyclopediaOffsets.Shipped;

        Assert.Equal(68, o.NameFor(hasBlip: true, hasIcon: true));
        Assert.Equal(39, o.IconXFor(hasBlip: true));
    }

    /// <summary>
    ///     No portrait either: the name takes the blip's own place, because nothing is left to its
    ///     left at all.
    /// </summary>
    [Fact]
    public void WithNeitherBlipNorPortrait_TheNameTakesTheBlipsPlace()
    {
        Assert.Equal(11, EncyclopediaOffsets.Shipped.NameFor(hasBlip: false, hasIcon: false));
    }

    /// <summary>A blip but no portrait closes only the portrait's share of the gap.</summary>
    [Fact]
    public void WithABlipButNoPortrait_TheNameClosesThePortraitsGap()
    {
        Assert.Equal(68 - (39 - 11),
            EncyclopediaOffsets.Shipped.NameFor(hasBlip: true, hasIcon: false));
    }
}
