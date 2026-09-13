// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     Text the engine copies into a fixed buffer with no length check.
/// </summary>
/// <remarks>
///     <para>
///         Two limits, both in <c>DatabaseMap.cpp</c>, and both the same shape: an assert, then an
///         unbounded <c>strcpy</c> into a stack buffer.
///     </para>
///     <list type="bullet">
///         <item>
///             A tag's VALUE goes into <c>strtok_string_buffer[8192]</c> -
///             <c>DatabaseMapClass::Map_Data_Of_Type</c>, line 5582, <c>CMP EAX, 0x2000</c>.
///         </item>
///         <item>
///             An object's NAME goes into a 128-byte buffer to be uppercased and hashed -
///             <c>GameObjectTypeClass::Get_Name_CRC</c>, <c>GameObjectType.cpp</c> line 2225.
///         </item>
///     </list>
///     <para>
///         The assert is what a debug build gives you. A release build has no check at all, so
///         overlong text runs off the end of a stack buffer - which is why this is an Error and why
///         the message talks about the build the player runs rather than the one Petroglyph had.
///     </para>
///     <para>
///         Measured on the shipped corpus: longest tag value 2,535 characters (an AI equation in
///         <c>Basiclandequations.xml</c>), longest object name 76. Vanilla is clear of both, but the
///         value limit is the one a mod can plausibly reach.
///     </para>
/// </remarks>
public sealed class EngineTextLimitTest
{
    private const string Uri = "file:///units/Units.xml";

    [Fact]
    public void A_value_at_the_limit_is_reported()
    {
        var fact = Assert.Single(Fact(new string('9', 8192)));

        Assert.Equal(8192, fact.Limit);
        Assert.Equal(8192, fact.ByteCount);
        // 8,192 bytes against a last-legal 8,191: exactly one character too many.
        Assert.Equal(1, fact.CharactersOver);
        Assert.Equal(XmlDiagnosticSeverity.Error, Report(fact).Severity);
    }

    /// <summary>The check is <c>size() &lt; 8192</c>, so 8,191 is the last legal length.</summary>
    [Fact]
    public void A_value_one_short_of_the_limit_is_not_an_error()
    {
        Assert.Equal(XmlDiagnosticSeverity.Warning,
            Report(Assert.Single(Fact(new string('9', 8191)))).Severity);
    }

    [Fact]
    public void An_ordinary_value_is_silent()
    {
        Assert.Empty(Fact("5.0"));
    }

    /// <summary>
    ///     The engine tests <c>std::string::size()</c> and copies BYTES. A value can sit well under
    ///     the limit in characters and still overflow - measuring characters would wave it through.
    /// </summary>
    [Fact]
    public void Multi_byte_text_is_measured_in_bytes()
    {
        // Each of these is three bytes in UTF-8, so 3,000 characters is 9,000 bytes - comfortably
        // under the limit by the wrong ruler and over it by the right one.
        var fact = Assert.Single(Fact(new string('\u20ac', 3000)));

        Assert.Equal(9000, fact.ByteCount);
        Assert.Equal(XmlDiagnosticSeverity.Error, Report(fact).Severity);
    }

    /// <summary>
    ///     The overage is CONVERTED, not relabelled. 3,000 three-byte characters is 9,000 bytes,
    ///     which is 809 bytes over - but only 270 characters need deleting to fix it, and telling
    ///     someone to remove 809 would have them delete three times what they need to.
    /// </summary>
    [Fact]
    public void The_overage_is_in_characters_not_bytes()
    {
        var fact = Assert.Single(Fact(new string('\u20ac', 3000)));

        Assert.Equal(270, fact.CharactersOver);
        Assert.Equal(9000, fact.ByteCount);
        Assert.Contains("270 characters too long", Report(fact).Message, StringComparison.Ordinal);
    }

    /// <summary>Bytes are the engine's unit and have no business in the message.</summary>
    [Theory]
    [InlineData(9000)]
    [InlineData(7500)]
    public void The_message_never_mentions_bytes(int length)
    {
        var message = Report(Assert.Single(Fact(new string('9', length)))).Message;

        Assert.DoesNotContain("byte", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("characters", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     These limits are reached by accumulation, so the warning arrives while there is still
    ///     room to act - and says how much room.
    /// </summary>
    [Fact]
    public void Approaching_the_limit_warns_with_the_headroom()
    {
        var d = Report(Assert.Single(Fact(new string('9', 7500))));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(DiagnosticIds.EngineTextLimitApproaching, d.Id);
        Assert.Contains("room for 691 more characters", d.Message, StringComparison.Ordinal);
    }

    /// <summary>Below the warning threshold nothing is said at all.</summary>
    [Fact]
    public void Comfortably_inside_the_limit_is_silent()
    {
        Assert.Empty(Fact(new string('9', 7000)));
    }

    /// <summary>How far over, in the unit the author can act on.</summary>
    [Fact]
    public void The_error_says_how_much_too_long_it_is()
    {
        Assert.Contains("809 characters too long", Report(Assert.Single(Fact(new string('9', 9000)))).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_report_names_the_tag()
    {
        Assert.Contains("Max_Speed", Assert.Single(Fact(new string('9', 9000))).Subject,
            StringComparison.Ordinal);
    }

    private static XmlDiagnosticResult Report(EngineTextLimitFact fact)
    {
        return Assert.Single(new EngineTextLimitHandler().Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }

    private static IReadOnlyList<EngineTextLimitFact> Fact(string value)
    {
        return Produce($"<Root><Max_Speed>{value}</Max_Speed></Root>")
            .OfType<EngineTextLimitFact>()
            .ToList();
    }

    private static IReadOnlyList<XmlFact> Produce(string xml)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new LimitSchema(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            []);

        return producer.Produce(xml, Uri);
    }

    private sealed class LimitSchema : ISchemaProvider
    {
        private static readonly XmlTagDefinition Speed = new()
        {
            Tag = "Max_Speed", ValueType = XmlValueType.Float,
        };

        public XmlTagDefinition? GetTag(string tagName)
        {
            return tagName.Equals("Max_Speed", StringComparison.OrdinalIgnoreCase) ? Speed : null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [Speed];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public EnumDefinition? GetEnum(string enumName)
        {
            return null;
        }

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }
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
