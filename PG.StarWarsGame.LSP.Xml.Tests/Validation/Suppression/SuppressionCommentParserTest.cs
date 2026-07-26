// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation.Suppression;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Suppression;

public sealed class SuppressionCommentParserTest
{
    private static readonly DiagnosticId Asset1 = new(DiagnosticGroup.Assets, 1);
    private static readonly DiagnosticId Asset2 = new(DiagnosticGroup.Assets, 2);

    private static IReadOnlyList<SuppressionRange> Parse(string xml)
    {
        return Scan(xml).Ranges;
    }

    private static SuppressionScan Scan(string xml)
    {
        return SuppressionCommentParser.Parse(
            XmlUtility.CreateHtmlDocument(xml),
            n => n.Name.Equals("unit", StringComparison.OrdinalIgnoreCase));
    }

    // ── node scope (the bare form) ───────────────────────────────────────────

    [Fact]
    public void BareDirective_CoversTheFollowingElementOnly()
    {
        const string xml = """
                           <Units>
                             <!-- aetswg:suppress aetswg-004-0001 model is generated at build time -->
                             <Model>missing.alo</Model>
                             <Texture>also_missing.tga</Texture>
                           </Units>
                           """;

        var range = Assert.Single(Parse(xml));

        Assert.Equal(SuppressionScope.Node, range.Scope);
        Assert.True(range.Covers(Asset1, 2)); // <Model>
        Assert.False(range.Covers(Asset1, 3)); // <Texture> is a different node
    }

    // The reason text is free-form and must not change what is matched.
    [Fact]
    public void ReasonText_IsIgnoredForMatching()
    {
        var range = Assert.Single(Parse(
            "<Units>\n<!-- aetswg:suppress aetswg-004-0001 because aetswg-004-0002 is the real one -->\n<Model/>\n</Units>"));

        Assert.True(range.Covers(Asset1, 2));
        Assert.False(range.Covers(Asset2, 2));
    }

    // ── object scope ─────────────────────────────────────────────────────────

    [Fact]
    public void ObjectDirective_CoversTheEnclosingObject()
    {
        const string xml = """
                           <Units>
                             <Unit Name="A">
                               <!-- aetswg:suppress-object aetswg-004-0001 -->
                               <Model>missing.alo</Model>
                               <Texture>missing.tga</Texture>
                             </Unit>
                             <Unit Name="B">
                               <Model>other.alo</Model>
                             </Unit>
                           </Units>
                           """;

        var range = Assert.Single(Parse(xml));

        Assert.Equal(SuppressionScope.Object, range.Scope);
        Assert.True(range.Covers(Asset1, 3));
        Assert.True(range.Covers(Asset1, 4));
        Assert.False(range.Covers(Asset1, 7)); // the second Unit is untouched
    }

    // ── file scope ───────────────────────────────────────────────────────────

    [Fact]
    public void FileDirective_CoversEveryLine()
    {
        var range = Assert.Single(Parse(
            "<Units>\n<!-- aetswg:suppress-file aetswg-004-* -->\n<Model/>\n</Units>"));

        Assert.Equal(SuppressionScope.File, range.Scope);
        Assert.True(range.Covers(Asset1, 0));
        Assert.True(range.Covers(Asset2, 100000));
    }

    // ── group wildcard ───────────────────────────────────────────────────────

    [Fact]
    public void GroupWildcard_CoversEveryIdInTheGroup()
    {
        var range = Assert.Single(Parse(
            "<Units>\n<!-- aetswg:suppress aetswg-004-* -->\n<Model/>\n</Units>"));

        Assert.True(range.Covers(Asset1, 2));
        Assert.True(range.Covers(Asset2, 2));
        Assert.False(range.Covers(new DiagnosticId(DiagnosticGroup.Story, 1), 2));
    }

    // ── degenerate placements ────────────────────────────────────────────────

    // A directive whose target does not exist must not widen to the rest of the document.
    // Suppressing more than was asked for is worse than suppressing nothing.
    [Fact]
    public void NodeDirectiveWithNothingAfterIt_CoversOnlyItsOwnLine()
    {
        var range = Assert.Single(Parse("<Units>\n<Model/>\n<!-- aetswg:suppress aetswg-004-0001 -->\n</Units>"));

        Assert.False(range.Covers(Asset1, 1));
        Assert.True(range.Covers(Asset1, 2));
        Assert.False(range.Covers(Asset1, 3));
    }

    [Fact]
    public void ObjectDirectiveOutsideAnyObject_CoversOnlyItsOwnLine()
    {
        var range = Assert.Single(Parse("<Units>\n<!-- aetswg:suppress-object aetswg-004-0001 -->\n<Model/>\n</Units>"));

        Assert.True(range.Covers(Asset1, 1));
        Assert.False(range.Covers(Asset1, 2));
    }

    // ── rule lists and reasons (grammar owned by SuppressionDirectiveParser) ──

    // One comment, several ids: the shadow diagnostics in particular are a pair users want
    // together. Each becomes its own range over the same lines.
    [Fact]
    public void ADirectiveNamingSeveralIds_ProducesOneRangePerId()
    {
        var ranges = Parse(
            "<Units>\n<!-- aetswg:suppress aetswg-004-0001, aetswg-004-0002 -->\n<Model/>\n</Units>");

        Assert.Equal(2, ranges.Count);
        Assert.All(ranges, r => Assert.Equal(ranges[0].StartLine, r.StartLine));
        Assert.All(ranges, r => Assert.Equal(ranges[0].EndLine, r.EndLine));
    }

    [Fact]
    public void EveryRangeFromOneDirective_CarriesItsReason()
    {
        var ranges = Parse(
            "<Units>\n<!-- aetswg:suppress aetswg-004-0001, aetswg-004-0002 reason:: built later -->\n" +
            "<Model/>\n</Units>");

        Assert.All(ranges, r => Assert.Equal("built later", r.Reason));
    }

    [Fact]
    public void ADirectiveWithoutAReason_LeavesItUnset()
    {
        var range = Assert.Single(Parse(
            "<Units>\n<!-- aetswg:suppress aetswg-004-0001 -->\n<Model/>\n</Units>"));

        Assert.Null(range.Reason);
    }

    // ── non-directives ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("<!-- just a comment -->")]
    [InlineData("<!-- aetswg:suppress -->")] // no id
    [InlineData("<!-- aetswg:suppress aetswg-4-1 -->")] // malformed id
    [InlineData("<!-- suppress aetswg-004-0001 -->")] // missing prefix
    [InlineData("<!-- lsp:suppress duplicate-symbol -->")] // removed pre-id form
    public void NonDirectiveComments_ProduceNothing(string comment)
    {
        Assert.Empty(Parse($"<Units>\n{comment}\n<Model/>\n</Units>"));
    }

    [Fact]
    public void MultipleDirectives_AreAllReturned()
    {
        const string xml = """
                           <Units>
                             <!-- aetswg:suppress-file aetswg-004-0001 -->
                             <!-- aetswg:suppress aetswg-004-0002 -->
                             <Model/>
                           </Units>
                           """;

        Assert.Equal(2, Parse(xml).Count);
    }

    // A directive is only a comment - it must not be mistaken for one when it names an id inside
    // ordinary element text.
    [Fact]
    public void DirectiveTextInsideAnElement_IsNotADirective()
    {
        Assert.Empty(Parse("<Units>\n<Note>aetswg:suppress aetswg-004-0001</Note>\n</Units>"));
    }

    // ── reported problems ────────────────────────────────────────────────────

    [Fact]
    public void AMistypedId_IsReportedAgainstTheCommentsOwnLine()
    {
        const string xml = """
                           <Units>
                             <!-- aetswg:suppress aetswg-4-1 -->
                             <Unit Name="A"/>
                           </Units>
                           """;

        var problem = Assert.Single(Scan(xml).Problems);

        Assert.Equal(1, problem.Line);
        Assert.Equal(DiagnosticIds.SuppressionUnknownRule, problem.Id);
    }

    [Fact]
    public void OrdinaryComments_ReportNothing()
    {
        Assert.Empty(Scan("<Units>\n  <!-- a note about the units -->\n</Units>").Problems);
    }

    [Fact]
    public void AWellFormedDirective_ReportsNothing()
    {
        Assert.Empty(Scan("<Units>\n  <!-- aetswg:suppress aetswg-004-0001 -->\n  <Unit/>\n</Units>")
            .Problems);
    }
}
