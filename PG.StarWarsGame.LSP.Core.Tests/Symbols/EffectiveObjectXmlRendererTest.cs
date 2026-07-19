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
        return new EffectiveObject("V", "SpaceUnit", true, false, null,
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

    // ── the replaced value is reported here rather than inline in the editor (#73) ───────────

    [Fact]
    public void Render_OverriddenTag_ShowsTheValueItReplaced()
    {
        var xml = EffectiveObjectXmlRenderer.Render(Object(
            TagWithBase("Tech_Level", "0", VariantProvenance.Overridden, "V", "99")));

        Assert.Contains("overrides base - was 99", xml);
    }

    [Fact]
    public void Render_MergedTag_ShowsWhatTheBaseContributes()
    {
        var xml = EffectiveObjectXmlRenderer.Render(Object(
            TagWithBase("Death_Clone", "Base_Clone, Hero_Clone", VariantProvenance.Merged, "V",
                "Base_Clone")));

        Assert.Contains("base contributes Base_Clone", xml);
    }

    [Fact]
    public void Render_BaseValueSpanningLines_IsCollapsedOntoTheCommentLine()
    {
        // A raw multi-line value would run past the "-->" and corrupt every following line.
        var xml = EffectiveObjectXmlRenderer.Render(Object(
            TagWithBase("List", "x", VariantProvenance.Overridden, "V", "a,\n    b,\r\n    c")));

        var note = xml.Split('\n').First(l => l.Contains("overrides base"));
        Assert.Contains("a, b, c", note);
        Assert.Contains("-->", note);
    }

    [Fact]
    public void Render_LongBaseValue_IsReportedInFull()
    {
        // The expansion is where the whole value is meant to be readable; truncating it here would
        // leave the replaced value visible nowhere (the inline marker is deliberately short).
        var longValue = string.Join(", ", Enumerable.Range(0, 40).Select(i => $"Entry_{i}"));
        var xml = EffectiveObjectXmlRenderer.Render(Object(
            TagWithBase("List", "x", VariantProvenance.Overridden, "V", longValue)));

        Assert.Contains(longValue, xml);
        Assert.DoesNotContain("…", xml);
    }

    [Fact]
    public void Render_BaseValueContainingDoubleDash_DoesNotTerminateTheComment()
    {
        // "--" is illegal inside an XML comment and would close it early.
        var xml = EffectiveObjectXmlRenderer.Render(Object(
            TagWithBase("Flag", "on", VariantProvenance.Overridden, "V", "a--b")));

        var note = xml.Split('\n').First(l => l.Contains("overrides base"));
        Assert.DoesNotContain("a--b", note);
        Assert.EndsWith("-->", note.TrimEnd('\r'));
    }

    [Fact]
    public void Render_AddedTag_ShowsNoReplacedValue()
    {
        var xml = EffectiveObjectXmlRenderer.Render(Object(
            Tag("Shield", "50", VariantProvenance.Added, "V")));

        Assert.Contains("added by variant", xml);
        Assert.DoesNotContain("was ", xml);
    }

    private static EffectiveTag TagWithBase(string name, string value, VariantProvenance prov,
        string originId, string baseValue)
    {
        return new EffectiveTag(name, value, $"<{name}>{value}</{name}>", prov, originId, null, baseValue);
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
        var obj = new EffectiveObject("A", "SpaceUnit", true, true, "A",
            ["A"], ImmutableArray<EffectiveTag>.Empty);

        var xml = EffectiveObjectXmlRenderer.Render(obj);

        Assert.Contains("cyclic", xml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_PreservesMultiLineFragment()
    {
        var tag = new EffectiveTag("Block", "x",
            "<Block>\n  <Inner>x</Inner>\n</Block>", VariantProvenance.Inherited, "B", null);
        var obj = new EffectiveObject("V", "SpaceUnit", true, false, null,
            ["V", "B"], [tag]);

        var xml = EffectiveObjectXmlRenderer.Render(obj);

        Assert.Contains("<Inner>x</Inner>", xml);
    }
}