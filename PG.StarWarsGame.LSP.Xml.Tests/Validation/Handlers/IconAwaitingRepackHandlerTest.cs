// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     The "drawn but never repacked" warning: art exists in a source folder while the workspace's
///     mega texture does not carry it, so the game would show nothing.
/// </summary>
public sealed class IconAwaitingRepackHandlerTest
{
    private static readonly IconAwaitingRepackHandler Sut = new();

    private static DiagnosticsContext Context(params string[] awaitingRepack)
    {
        return new DiagnosticsContext(
            new EmptySchemaProvider(), GameIndex.Empty, "file:///u.xml", "en",
            awaitingRepack.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static XmlTagValueFact Fact(string tagName, string value)
    {
        return XmlHandlerTestFixtures.MakeFact(
            XmlHandlerTestFixtures.MakeTag(tagName, XmlValueType.NameReference), value);
    }

    private static IReadOnlyList<XmlDiagnosticResult> Run(DiagnosticsContext ctx, XmlTagValueFact fact)
    {
        return Sut.Handle(fact, ctx).ToList();
    }

    [Fact]
    public void Warns_WhenTheIconIsAwaitingARepack()
    {
        var results = Run(Context("I_BUTTON_LUKE"), Fact("Icon_Name", "I_BUTTON_LUKE.TGA"));

        var result = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, result.Severity);
        Assert.Contains("missing from this project's mega texture", result.Message);
        Assert.Contains("Rebuild the .mtd", result.Message);
    }

    // A directory records entries with a .TGA suffix whatever the packer was fed, while raw sources
    // are tracked by base name - so the comparison has to ignore the extension either way round.
    [Theory]
    [InlineData("I_BUTTON_LUKE.TGA")]
    [InlineData("I_BUTTON_LUKE")]
    [InlineData("i_button_luke.tga")]
    public void Warns_RegardlessOfHowTheNameIsWritten(string written)
    {
        Assert.Single(Run(Context("I_BUTTON_LUKE"), Fact("Icon_Name", written)));
    }

    [Fact]
    public void Silent_WhenTheIconIsPacked()
    {
        Assert.Empty(Run(Context("I_BUTTON_OTHER"), Fact("Icon_Name", "I_BUTTON_LUKE.TGA")));
    }

    // No workspace mega texture means nothing to be out of sync with; the set arrives empty.
    [Fact]
    public void Silent_WhenNothingIsAwaitingARepack()
    {
        Assert.Empty(Run(Context(), Fact("Icon_Name", "I_BUTTON_LUKE.TGA")));
    }

    // Null is the shape every existing construction site produces, including all the tests that
    // predate this handler - it must never fire there.
    [Fact]
    public void Silent_WhenTheContextCarriesNoIconInformation()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), GameIndex.Empty, "file:///u.xml", "en");

        Assert.Empty(Run(ctx, Fact("Icon_Name", "I_BUTTON_LUKE.TGA")));
    }

    // Scoped to Icon_Name: other texture-valued tags are not packed into the mega texture.
    [Fact]
    public void Silent_ForOtherTags()
    {
        Assert.Empty(Run(Context("I_BUTTON_LUKE"), Fact("Texture_Name", "I_BUTTON_LUKE.TGA")));
    }

    [Fact]
    public void Silent_ForAnEmptyValue()
    {
        Assert.Empty(Run(Context("I_BUTTON_LUKE"), Fact("Icon_Name", "   ")));
    }
}
