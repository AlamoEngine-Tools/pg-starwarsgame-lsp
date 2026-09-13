// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.CrossTagRules;

/// <summary>
///     A system spy's duration, whose legal range flips with its activation style.
/// </summary>
/// <remarks>
///     <para>
///         <c>SystemSpyAbilityClass::Validate_Data</c> branches on the style and demands the
///         OPPOSITE sign in each arm: <c>Galactic_Automatic</c> requires a negative duration (a
///         permanent effect) and repairs to <c>-1.0</c>; <c>Ground_Activated</c> requires a positive
///         one and repairs to <c>30.0</c>. No other style is supported.
///     </para>
///     <para>
///         A range rule on the tag could not express this - the same value is correct under one
///         style and refused under the other - which is why it is a cross-tag rule and why the first
///         harvest, which keyed on the tag column, never surfaced it.
///     </para>
/// </remarks>
public sealed class SystemSpyDurationRuleTest
{
    private const string Uri = "file:///abilities/Abilities.xml";

    /// <summary>Permanent effects are negative; zero and above are refused.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("30")]
    public void Galactic_automatic_refuses_a_non_negative_duration(string duration)
    {
        var d = Report("Galactic_Automatic", duration);

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Equal("-1.0", d.SuggestedFix);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-0.5")]
    public void Galactic_automatic_accepts_a_negative_duration(string duration)
    {
        Assert.Empty(Facts("Galactic_Automatic", duration));
    }

    /// <summary>And the other way round for the activated style.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Ground_activated_refuses_a_non_positive_duration(string duration)
    {
        var d = Report("Ground_Activated", duration);

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Equal("30.0", d.SuggestedFix);
    }

    [Fact]
    public void Ground_activated_accepts_a_positive_duration()
    {
        Assert.Empty(Facts("Ground_Activated", "45"));
    }

    /// <summary>
    ///     A style the class does not support is the allowed-values check's business, not this
    ///     rule's - guessing which bound applies would be inventing one.
    /// </summary>
    [Fact]
    public void An_unsupported_style_is_left_alone()
    {
        Assert.Empty(Facts("User_Input", "30"));
    }

    /// <summary>With no style written there is no bound to choose between.</summary>
    [Fact]
    public void A_missing_style_is_left_alone()
    {
        var body = "<System_Spy_Ability Name='S'><Duration_In_Secs>30</Duration_In_Secs>"
                   + "</System_Spy_Ability>";

        Assert.Empty(Produce(body).OfType<SystemSpyDurationFact>());
    }

    [Fact]
    public void A_non_numeric_duration_is_left_to_the_type_handler()
    {
        Assert.Empty(Facts("Ground_Activated", "soon"));
    }

    private static XmlDiagnosticResult Report(string style, string duration)
    {
        return Assert.Single(new SystemSpyDurationHandler()
            .Handle(Assert.Single(Facts(style, duration)), XmlHandlerTestFixtures.EmptyCtx));
    }

    private static IReadOnlyList<SystemSpyDurationFact> Facts(string style, string duration)
    {
        var body = "<System_Spy_Ability Name='S'>"
                   + $"<Activation_Style>{style}</Activation_Style>"
                   + $"<Duration_In_Secs>{duration}</Duration_In_Secs></System_Spy_Ability>";

        return Produce(body).OfType<SystemSpyDurationFact>().ToList();
    }

    private static IReadOnlyList<XmlFact> Produce(string body)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            [new SystemSpyDurationRule()]);

        return producer.Produce($"<Root>{body}</Root>", Uri);
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
