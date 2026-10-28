// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;
using PG.StarWarsGame.LSP.Server.Preview.Sections;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview.Sections;

/// <summary>The tag readings every section builder shares (#314), on written values as the XML has them.</summary>
public sealed class PreviewTagReaderTest
{
    private static EffectiveObject Object(params (string Tag, string Value)[] tags)
    {
        var tagSource = new FakeVariantTagSource().With("Unit",
            tags.Select(t => new VariantTag(t.Tag, t.Value, $"<{t.Tag}>{t.Value}</{t.Tag}>", 0)).ToArray());
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("Unit",
            [
                new GameSymbol("Unit", GameSymbolKind.XmlObject, "SpaceUnit",
                    new FileOrigin("file:///unit.xml", 0, 0), null, null)
            ])
        };
        return new EffectiveObjectResolver(index, new SchemaProviderBuilder().Build(), tagSource).Resolve("Unit");
    }

    [Fact]
    public void Colour_ReadsFourChannels_AndDefaultsAlphaToOpaque()
    {
        Assert.Equal(new PreviewRgba(75, 75, 75, 255),
            PreviewTagReader.Colour(Object(("Color", "75, 75, 75, 255")), "Color"));
        Assert.Equal(new PreviewRgba(1, 2, 3, 255), PreviewTagReader.Colour(Object(("Color", "1 2 3")), "Color"));
        Assert.Equal(new PreviewRgba(255, 0, 0, 255),
            PreviewTagReader.Colour(Object(("Color", "300, -4, 0")), "Color"));
    }

    [Fact]
    public void Colour_IsNull_ForAnAbsentTag_TooFewTokens_OrANonNumber()
    {
        Assert.Null(PreviewTagReader.Colour(Object(), "Color"));
        Assert.Null(PreviewTagReader.Colour(Object(("Color", "1, 2")), "Color"));
        Assert.Null(PreviewTagReader.Colour(Object(("Color", "1, two, 3")), "Color"));
    }

    [Fact]
    public void FirstToken_TakesTheFirstOfAList_TrimmedAndNullWhenEmpty()
    {
        Assert.Equal("Neutral", PreviewTagReader.FirstToken(" Neutral, Rebel, Empire "));
        Assert.Null(PreviewTagReader.FirstToken(""));
        Assert.Null(PreviewTagReader.FirstToken(null));
    }

    [Fact]
    public void CategoryTokens_SplitOnBarOrComma_Distinct_AndEmptyForNothing()
    {
        Assert.Equal(["Frigate", "Corvette"], PreviewTagReader.CategoryTokens("Frigate | Corvette, frigate"));
        Assert.Empty(PreviewTagReader.CategoryTokens("  "));
        Assert.Empty(PreviewTagReader.CategoryTokens(null));
    }

    [Fact]
    public void Vector3Tag_ReadsExactlyThreeNumbers()
    {
        Assert.Equal(new PreviewVector3(1f, -2.5f, 3f), PreviewTagReader.Vector3Tag(Object(("V", "1, -2.5, 3")), "V"));
        Assert.Null(PreviewTagReader.Vector3Tag(Object(("V", "1, 2")), "V"));
        Assert.Null(PreviewTagReader.Vector3Tag(Object(), "V"));
    }

    [Fact]
    public void Numbers_KeepsTheTokensThatParse()
    {
        Assert.Equal([1, 3], PreviewTagReader.Numbers<int>("1, x, 3", int.TryParse));
        Assert.Empty(PreviewTagReader.Numbers<float>(null, float.TryParse));
    }

    [Fact]
    public void Blank_TurnsTheEmptyStringIntoNull()
    {
        Assert.Null(PreviewTagReader.Blank(""));
        Assert.Equal("a", PreviewTagReader.Blank("a"));
    }
}