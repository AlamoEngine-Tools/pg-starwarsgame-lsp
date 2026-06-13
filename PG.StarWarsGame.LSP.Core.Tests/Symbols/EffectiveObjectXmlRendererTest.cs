// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

public sealed class EffectiveObjectXmlRendererTest
{
    private static EffectiveTag Tag(string name, string value, VariantProvenance prov, string originId)
    {
        return new EffectiveTag(name, value, $"<{name}>{value}</{name}>", prov, originId, null);
    }

    private static EffectiveObject Object(params EffectiveTag[] tags)
    {
        return new EffectiveObject("V", "SpaceUnit", Found: true, Cyclic: false, null,
            ["V", "B"], tags.ToImmutableArray());
    }

    [Fact]
    public void Render_WrapsTagsInTypedElementWithName()
    {
        var xml = EffectiveObjectXmlRenderer.Render(
            Object(Tag("Max_Health", "100", VariantProvenance.Inherited, "B")));

        Assert.Contains("<SpaceUnit Name=\"V\">", xml);
        Assert.Contains("</SpaceUnit>", xml);
        Assert.Contains("<Max_Health>100</Max_Health>", xml);
    }

    [Fact]
    public void Render_AnnotatesInheritedTagWithSourceObject()
    {
        var xml = EffectiveObjectXmlRenderer.Render(
            Object(Tag("Max_Health", "100", VariantProvenance.Inherited, "B")));

        Assert.Contains("inherited from B", xml);
    }

    [Fact]
    public void Render_AnnotatesOverriddenAndAddedAndMerged()
    {
        var xml = EffectiveObjectXmlRenderer.Render(Object(
            Tag("A", "1", VariantProvenance.Overridden, "V"),
            Tag("B", "2", VariantProvenance.Added, "V"),
            Tag("C", "3", VariantProvenance.Merged, "V")));

        Assert.Contains("overrides base", xml);
        Assert.Contains("added by variant", xml);
        Assert.Contains("merged with base", xml);
    }

    [Fact]
    public void Render_IncludesVariantChainComment()
    {
        var xml = EffectiveObjectXmlRenderer.Render(
            Object(Tag("X", "1", VariantProvenance.Inherited, "B")));

        Assert.Contains("V -> B", xml);
    }

    [Fact]
    public void Render_CyclicObject_IncludesWarning()
    {
        var obj = new EffectiveObject("A", "SpaceUnit", Found: true, Cyclic: true, "A",
            ["A"], ImmutableArray<EffectiveTag>.Empty);

        var xml = EffectiveObjectXmlRenderer.Render(obj);

        Assert.Contains("cyclic", xml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_PreservesMultiLineFragment()
    {
        var tag = new EffectiveTag("Block", "x",
            "<Block>\n  <Inner>x</Inner>\n</Block>", VariantProvenance.Inherited, "B", null);
        var obj = new EffectiveObject("V", "SpaceUnit", Found: true, Cyclic: false, null,
            ["V", "B"], [tag]);

        var xml = EffectiveObjectXmlRenderer.Render(obj);

        Assert.Contains("<Inner>x</Inner>", xml);
    }
}
