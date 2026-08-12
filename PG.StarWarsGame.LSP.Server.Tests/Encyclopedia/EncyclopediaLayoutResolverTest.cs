// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Encyclopedia;

namespace PG.StarWarsGame.LSP.Server.Tests.Encyclopedia;

/// <summary>
///     The popup's geometry, fonts and colours, read from the <c>encyclopedia_*</c>
///     CommandBarComponents. The base game's values are the floor; a mod that ships its own
///     Commandbarcomponents.xml overrides them.
/// </summary>
public sealed class EncyclopediaLayoutResolverTest
{
    private static GameSymbol Component(string id)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "CommandBarComponent",
            new FileOrigin("file:///Commandbarcomponents.xml", 0, 0), null, null);
    }

    private static GameIndex IndexWith(params string[] componentIds)
    {
        var defs = componentIds
            .Select(Component)
            .ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with { WorkspaceDefinitions = defs };
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    private static EncyclopediaLayout Resolve(GameIndex index, FakeVariantTagSource source)
    {
        return new EncyclopediaLayoutResolver(
            new EffectiveObjectResolver(index, new NullSchemaProvider(), source)).Resolve();
    }

    // ── the base-game floor ──────────────────────────────────────────────────

    [Fact]
    public void Resolve_NothingIndexed_UsesBaseGameDefaults()
    {
        // No workspace and no baseline: the preview still has to draw something, and the shipped
        // values are the honest answer.
        var layout = Resolve(GameIndex.Empty, new FakeVariantTagSource());

        Assert.Equal(262d, layout.Width);
        Assert.Equal(14d, layout.RowHeight);
        Assert.Equal("Arial", layout.Body.FontName);
        Assert.Equal(7d, layout.Body.FontPointSize);
        Assert.Equal("Arial Bold", layout.Header.FontName);
        Assert.Equal(new EncyclopediaRgba(192, 192, 192, 200), layout.Body.TextColor);
        Assert.Equal(new EncyclopediaRgba(255, 255, 255, 255), layout.Header.TextColor);
        Assert.Equal(new EncyclopediaRgba(255, 255, 255, 128), layout.BackdropColor);
    }

    // ── mod overrides ────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_ModWidensBackComponent_AdoptsModWidth()
    {
        var index = IndexWith("encyclopedia_back");
        var source = new FakeVariantTagSource()
            .With("encyclopedia_back", Tag("Size", "400 20"), Tag("Color", "10 20 30 40"));

        var layout = Resolve(index, source);

        Assert.Equal(400d, layout.Width);
        Assert.Equal(20d, layout.RowHeight);
        Assert.Equal(new EncyclopediaRgba(10, 20, 30, 40), layout.BackdropColor);
    }

    [Fact]
    public void Resolve_ModComponentOmitsFont_KeepsBaseGameFontForThatTag()
    {
        // A mod that only re-colours the body must not lose the font: the fallback is per-tag, so
        // an incomplete component inherits the shipped value rather than rendering with nothing.
        var index = IndexWith("encyclopedia_text");
        var source = new FakeVariantTagSource()
            .With("encyclopedia_text", Tag("Text_Color", "1 2 3 4"));

        var layout = Resolve(index, source);

        Assert.Equal(new EncyclopediaRgba(1, 2, 3, 4), layout.Body.TextColor);
        Assert.Equal("Arial", layout.Body.FontName);
        Assert.Equal(7d, layout.Body.FontPointSize);
    }

    [Fact]
    public void Resolve_ModChangesFontAndScale_AdoptsBoth()
    {
        var index = IndexWith("encyclopedia_text");
        var source = new FakeVariantTagSource()
            .With("encyclopedia_text",
                Tag("Font_Name", "Verdana"), Tag("Font_Point_Size", "9"), Tag("Scale", "1.5"));

        var layout = Resolve(index, source);

        Assert.Equal("Verdana", layout.Body.FontName);
        Assert.Equal(9d, layout.Body.FontPointSize);
        Assert.Equal(1.5d, layout.Body.Scale);
    }

    // ── the portrait's draw scale ────────────────────────────────────────────

    [Fact]
    public void Resolve_NothingIndexed_UsesBaseGameIconScale()
    {
        // encyclopedia_icon's Size is "0.75 0.66", and the shipped comment says X is the scale of
        // the icon in the unit header. The icon asset is 50px, so the header draws it at 37.5.
        Assert.Equal(0.75d, Resolve(GameIndex.Empty, new FakeVariantTagSource()).IconScale);
    }

    [Fact]
    public void Resolve_ModRescalesIcon_AdoptsModScale()
    {
        var index = IndexWith("encyclopedia_icon");
        var source = new FakeVariantTagSource()
            .With("encyclopedia_icon", Tag("Size", "1.0 0.5"));

        Assert.Equal(1.0d, Resolve(index, source).IconScale);
    }

    [Fact]
    public void Resolve_IconSizeUnparsable_KeepsBaseGameScale()
    {
        var index = IndexWith("encyclopedia_icon");
        var source = new FakeVariantTagSource().With("encyclopedia_icon", Tag("Size", "big"));

        Assert.Equal(0.75d, Resolve(index, source).IconScale);
    }

    // ── alignment is encoded as presence ─────────────────────────────────────

    [Fact]
    public void Resolve_JustifyTagsAbsent_MeansCentered()
    {
        // encyclopedia_center_text carries neither justify tag in the shipped file - that absence
        // is the whole encoding, so it must not be read as "defaults to left".
        var layout = Resolve(GameIndex.Empty, new FakeVariantTagSource());

        Assert.Equal(EncyclopediaTextAlignment.Center, layout.CenterText.Alignment);
        Assert.Equal(EncyclopediaTextAlignment.Left, layout.Body.Alignment);
        Assert.Equal(EncyclopediaTextAlignment.Right, layout.RightText.Alignment);
    }

    [Fact]
    public void Resolve_ModSetsRightJustified_OverridesLeftDefault()
    {
        var index = IndexWith("encyclopedia_text");
        var source = new FakeVariantTagSource()
            .With("encyclopedia_text", Tag("Right_Justified", "True"));

        var layout = Resolve(index, source);

        Assert.Equal(EncyclopediaTextAlignment.Right, layout.Body.Alignment);
    }

    [Fact]
    public void Resolve_ModTurnsLeftJustifiedOff_FallsBackToCentered()
    {
        var index = IndexWith("encyclopedia_text");
        var source = new FakeVariantTagSource()
            .With("encyclopedia_text", Tag("Left_Justified", "False"));

        var layout = Resolve(index, source);

        Assert.Equal(EncyclopediaTextAlignment.Center, layout.Body.Alignment);
    }

    // ── malformed values ─────────────────────────────────────────────────────

    [Fact]
    public void Resolve_UnparsableSize_KeepsBaseGameWidth()
    {
        // Width drives the wrap points, so a junk value must not collapse the card to zero.
        var index = IndexWith("encyclopedia_back");
        var source = new FakeVariantTagSource().With("encyclopedia_back", Tag("Size", "wide"));

        var layout = Resolve(index, source);

        Assert.Equal(262d, layout.Width);
    }

    [Fact]
    public void Resolve_ColourWithTooFewChannels_KeepsBaseGameColour()
    {
        var index = IndexWith("encyclopedia_text");
        var source = new FakeVariantTagSource().With("encyclopedia_text", Tag("Text_Color", "1 2"));

        var layout = Resolve(index, source);

        Assert.Equal(new EncyclopediaRgba(192, 192, 192, 200), layout.Body.TextColor);
    }
}
