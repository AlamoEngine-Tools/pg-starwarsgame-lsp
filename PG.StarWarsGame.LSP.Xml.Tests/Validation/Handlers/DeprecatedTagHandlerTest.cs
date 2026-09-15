// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class DeprecatedTagHandlerTest
{
    private static readonly DeprecatedTagHandler Sut = new();

    private static XmlTagDefinition DeprecatedTag(string name)
    {
        return new XmlTagDefinition
        {
            Tag = name, ValueType = XmlValueType.Float, Deprecated = true
        };
    }

    private static XmlTagDefinition ActiveTag(string name)
    {
        return new XmlTagDefinition
        {
            Tag = name, ValueType = XmlValueType.Float, Deprecated = false
        };
    }

    [Fact]
    public void Deprecated_tag_emits_warning()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(DeprecatedTag("Old_Field"), "1.0");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("Old_Field", d.Message);
    }

    [Fact]
    public void Non_deprecated_tag_emits_no_diagnostics()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(ActiveTag("Current_Field"), "1.0");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    /// <summary>
    ///     "Do not use this" without a reason is the least actionable thing a diagnostic can say,
    ///     and the schema usually knows the reason already - it is in the tag's own description.
    /// </summary>
    /// <remarks>
    ///     Prompted by <c>Standalone_Space_Maps_Special_Weapon_B</c> (issue #99), which is
    ///     deprecated because "Multiple special weapons are not supported by the game engine". The
    ///     author of a mod hitting the old message had no way to learn that from the editor.
    /// </remarks>
    [Fact]
    public void Deprecated_tag_carries_the_schema_reason_when_there_is_one()
    {
        var tag = new XmlTagDefinition
        {
            Tag = "Old_Field",
            ValueType = XmlValueType.Float,
            Deprecated = true,
            Description = new Dictionary<string, string>
            {
                ["en"] = "Multiple special weapons are not supported by the game engine."
            }
        };

        var fact = XmlHandlerTestFixtures.MakeFact(tag, "1.0");
        var d = Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList());

        Assert.Contains("Old_Field", d.Message);
        Assert.Contains("Multiple special weapons are not supported", d.Message);
    }

    // A description in another language is not the reason for THIS reader, so it must not be
    // pasted in regardless - the message falls back to the bare form.
    [Fact]
    public void Deprecated_tag_ignores_a_description_in_another_locale()
    {
        var tag = new XmlTagDefinition
        {
            Tag = "Old_Field",
            ValueType = XmlValueType.Float,
            Deprecated = true,
            Description = new Dictionary<string, string> { ["de"] = "Nicht mehr verwenden." }
        };

        var fact = XmlHandlerTestFixtures.MakeFact(tag, "1.0");
        var d = Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList());

        Assert.DoesNotContain("Nicht mehr", d.Message);
    }

    [Theory]
    [InlineData(XmlValueType.Float)]
    [InlineData(XmlValueType.Int)]
    [InlineData(XmlValueType.NameReference)]
    [InlineData(XmlValueType.Boolean)]
    public void Deprecated_tag_emits_warning_regardless_of_value_type(XmlValueType type)
    {
        var tag = new XmlTagDefinition { Tag = "Old", ValueType = type, Deprecated = true };
        var fact = XmlHandlerTestFixtures.MakeFact(tag, "value");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
    }
}