// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     The two tags whose spelling the engine matches byte for byte.
/// </summary>
/// <remarks>
///     <para>
///         Nearly every tag is case-insensitive, because the database mapper uppercases the key
///         before matching. <c>StoryModeClass::Load_Plots</c> never reaches that mapper - it
///         compares with <c>std::operator==</c>, which is a memcmp.
///     </para>
///     <para>
///         The two fail differently, and the rule has to say so. The result of the
///         <c>Active_Plot</c> comparison ALONE becomes the <c>is_active</c> argument to
///         <c>Load_Single_Plot</c>, so a mis-cased <c>Active_Plot</c> loads as SUSPENDED - a plot
///         that exists and never starts. A mis-cased <c>Suspended_Plot</c> lands on that same
///         default and is therefore right by accident.
///     </para>
///     <para>
///         Vanilla is uniformly canonical - 216 and 869 occurrences, no variants - so the corpus
///         could never have suggested this rule, and it is silent on shipped data.
///     </para>
/// </remarks>
public sealed class CaseSensitiveTagTest
{
    private const string Uri = "file:///story/Story_Plots.xml";

    [Theory]
    [InlineData("Active_Plot")]
    [InlineData("Suspended_Plot")]
    public void The_canonical_spelling_is_silent(string tag)
    {
        Assert.Empty(Facts($"<Root><Plot><{tag}>P_01</{tag}></Plot></Root>"));
    }

    /// <summary>
    ///     The damaging one: the plot loads, and never runs. Error, because nothing in the game
    ///     will tell the author - the campaign simply does not happen.
    /// </summary>
    [Theory]
    [InlineData("active_plot")]
    [InlineData("ACTIVE_PLOT")]
    [InlineData("Active_plot")]
    public void A_mis_cased_active_plot_is_an_error(string tag)
    {
        var fact = Assert.Single(Facts($"<Root><Plot><{tag}>P_01</{tag}></Plot></Root>"));

        Assert.Equal(tag, fact.Authored);
        Assert.Equal("Active_Plot", fact.Expected);

        var d = Report(fact);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("suspended", d.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The harmless one, reported anyway at a lower severity: it works only because an
    ///     unrecognised tag falls on the same default, which is not something to rely on.
    /// </summary>
    [Theory]
    [InlineData("suspended_plot")]
    [InlineData("SUSPENDED_PLOT")]
    public void A_mis_cased_suspended_plot_is_a_warning(string tag)
    {
        var fact = Assert.Single(Facts($"<Root><Plot><{tag}>P_01</{tag}></Plot></Root>"));

        Assert.Equal("Suspended_Plot", fact.Expected);
        Assert.Equal(XmlDiagnosticSeverity.Warning, Report(fact).Severity);
    }

    /// <summary>
    ///     Every other tag in the game IS case-insensitive, and saying otherwise would be a lie
    ///     with 3,000 instances.
    /// </summary>
    [Theory]
    [InlineData("max_speed")]
    [InlineData("MAX_SPEED")]
    public void Other_tags_are_left_alone(string tag)
    {
        Assert.Empty(Facts($"<Root><Unit><{tag}>5.0</{tag}></Unit></Root>"));
    }

    private static XmlDiagnosticResult Report(CaseSensitiveTagFact fact)
    {
        return Assert.Single(new CaseSensitiveTagHandler().Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }

    private static IReadOnlyList<CaseSensitiveTagFact> Facts(string xml)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            []);

        return producer.Produce(xml, Uri).OfType<CaseSensitiveTagFact>().ToList();
    }
}

file sealed class EmptyFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string fileUri)
    {
    }
}