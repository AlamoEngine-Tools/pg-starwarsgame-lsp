// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

file sealed class StubEnumSchemaProvider : ISchemaProvider
{
    private readonly Dictionary<string, EnumDefinition> _enums;

    public StubEnumSchemaProvider(params EnumDefinition[] enums)
    {
        _enums = enums.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
    }

    public EnumDefinition? GetEnum(string name) => _enums.GetValueOrDefault(name);
    public IReadOnlyList<EnumDefinition> AllEnums => [.. _enums.Values];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public XmlTagDefinition? GetTag(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
    public GameObjectTypeDefinition? GetObjectType(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];
    public event EventHandler? SchemaRefreshed { add { } remove { } }
}

public sealed class StoryParserDiagnosticCollectorTest
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string Xml(string inner) =>
        $"<StoryParser><Event>{inner}</Event></StoryParser>";

    private static EnumDefinition MakeEnum(string name, params string[] values) => new()
    {
        Name = name,
        Kind = EnumKind.SchemaFixed,
        Values = [.. values.Select(v => new EnumValueDefinition { Name = v })]
    };

    private static StoryParserDiagnosticCollector BuildCollector(params EnumDefinition[] enums)
        => new(new StubEnumSchemaProvider(enums));

    private static GameIndex IndexWithSymbol(string id, string typeName)
    {
        var sym = new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new UnknownOrigin("test"), null);
        return GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with
            {
                Symbols = ImmutableDictionary<string, GameSymbol>.Empty.Add(id, sym)
            }
        };
    }

    // StoryFlagCompareMethod for STORY_FLAG tests
    private static EnumDefinition FlagCompareMethod => MakeEnum("StoryFlagCompareMethod",
        "GREATER_THAN", "LESS_THAN", "EQUAL_TO", "GREATER_THAN_EQUAL_TO", "LESS_THAN_EQUAL_TO");

    // StoryGenericTriggerType for STORY_GENERIC tests
    private static EnumDefinition GenericTriggerType => MakeEnum("StoryGenericTriggerType",
        "END_SETUP", "CLOSE_STORY_DIALOG", "CLICK");

    // -----------------------------------------------------------------------
    // Unused param — warning
    // -----------------------------------------------------------------------

    [Fact]
    public void Unused_event_param_produces_warning()
    {
        // STORY_ACCUMULATE has only 1 param; Event_Param2 is spurious
        var sut = BuildCollector();
        var xml = Xml("<Event_Type>STORY_ACCUMULATE</Event_Type>" +
                      "<Event_Param1>1000</Event_Param1>" +
                      "<Event_Param2>extra</Event_Param2>" +
                      "<Reward_Type>ZOOM_IN</Reward_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("Event_Param2") &&
                                     d.Message.Contains("STORY_ACCUMULATE"));
    }

    [Fact]
    public void Unused_reward_param_produces_warning()
    {
        // ZOOM_IN has 0 params; Reward_Param1 is spurious
        var sut = BuildCollector();
        var xml = Xml("<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                      "<Reward_Type>ZOOM_IN</Reward_Type>" +
                      "<Reward_Param1>extra</Reward_Param1>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("Reward_Param1") &&
                                     d.Message.Contains("ZOOM_IN"));
    }

    // -----------------------------------------------------------------------
    // Absent required param — warning
    // -----------------------------------------------------------------------

    [Fact]
    public void Missing_required_event_param_produces_warning()
    {
        // STORY_FLAG requires Param1 (FlagName), Param2 (Integer), Param3 (Enum)
        var sut = BuildCollector(FlagCompareMethod);
        var xml = Xml("<Event_Type>STORY_FLAG</Event_Type>" +
                      "<Event_Param1>MY_FLAG</Event_Param1>" +
                      "<Event_Param2>5</Event_Param2>" +
                      // Param3 (required Enum) is intentionally absent
                      "<Reward_Type>ZOOM_IN</Reward_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("STORY_FLAG") &&
                                     d.Message.Contains("Event_Param3"));
    }

    [Fact]
    public void Missing_required_reward_param_produces_warning()
    {
        // CREDITS requires Reward_Param1 (PositiveInteger)
        var sut = BuildCollector();
        var xml = Xml("<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                      "<Reward_Type>CREDITS</Reward_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("CREDITS") &&
                                     d.Message.Contains("Reward_Param1"));
    }

    // -----------------------------------------------------------------------
    // Enum validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Enum_param_with_wrong_value_produces_warning()
    {
        var sut = BuildCollector(FlagCompareMethod);
        var xml = Xml("<Event_Type>STORY_FLAG</Event_Type>" +
                      "<Event_Param1>MY_FLAG</Event_Param1>" +
                      "<Event_Param2>5</Event_Param2>" +
                      "<Event_Param3>NOT_A_COMPARE_METHOD</Event_Param3>" +
                      "<Reward_Type>ZOOM_IN</Reward_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("NOT_A_COMPARE_METHOD"));
    }

    [Fact]
    public void Enum_param_with_correct_value_produces_no_diagnostic()
    {
        var sut = BuildCollector(FlagCompareMethod);
        var xml = Xml("<Event_Type>STORY_FLAG</Event_Type>" +
                      "<Event_Param1>MY_FLAG</Event_Param1>" +
                      "<Event_Param2>5</Event_Param2>" +
                      "<Event_Param3>EQUAL_TO</Event_Param3>" +
                      "<Reward_Type>ZOOM_IN</Reward_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.DoesNotContain(diags, d => d.Message.Contains("EQUAL_TO"));
    }

    [Fact]
    public void Enum_param_with_correct_value_case_insensitive_produces_no_diagnostic()
    {
        var sut = BuildCollector(FlagCompareMethod);
        var xml = Xml("<Event_Type>STORY_FLAG</Event_Type>" +
                      "<Event_Param1>MY_FLAG</Event_Param1>" +
                      "<Event_Param2>5</Event_Param2>" +
                      "<Event_Param3>equal_to</Event_Param3>" +
                      "<Reward_Type>ZOOM_IN</Reward_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.DoesNotContain(diags, d => d.Message.Contains("equal_to"));
    }

    // -----------------------------------------------------------------------
    // EnumList validation
    // -----------------------------------------------------------------------

    [Fact]
    public void EnumList_valid_tokens_produce_no_diagnostic()
    {
        var sut = BuildCollector(GenericTriggerType);
        var xml = Xml("<Event_Type>STORY_GENERIC</Event_Type>" +
                      "<Event_Param1>END_SETUP CLICK</Event_Param1>" +
                      "<Reward_Type>ZOOM_IN</Reward_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Empty(diags);
    }

    [Fact]
    public void EnumList_invalid_token_produces_warning()
    {
        var sut = BuildCollector(GenericTriggerType);
        var xml = Xml("<Event_Type>STORY_GENERIC</Event_Type>" +
                      "<Event_Param1>END_SETUP INVALID_TOKEN</Event_Param1>" +
                      "<Reward_Type>ZOOM_IN</Reward_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("INVALID_TOKEN"));
    }

    // -----------------------------------------------------------------------
    // BooleanInt validation
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("FALSE")]
    public void BooleanInt_valid_values_produce_no_diagnostic(string value)
    {
        // LOCK_CONTROLS Reward_Param1 is BooleanInt required
        var sut = BuildCollector();
        var xml = Xml("<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                      "<Reward_Type>LOCK_CONTROLS</Reward_Type>" +
                      $"<Reward_Param1>{value}</Reward_Param1>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.DoesNotContain(diags, d => d.Message.Contains(value));
    }

    [Fact]
    public void BooleanInt_invalid_value_produces_warning()
    {
        var sut = BuildCollector();
        var xml = Xml("<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                      "<Reward_Type>LOCK_CONTROLS</Reward_Type>" +
                      "<Reward_Param1>yes</Reward_Param1>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("yes"));
    }

    // -----------------------------------------------------------------------
    // Integer / PositiveInteger validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Integer_invalid_value_produces_warning()
    {
        // STORY_FLAG Param2 is Integer required
        var sut = BuildCollector(FlagCompareMethod);
        var xml = Xml("<Event_Type>STORY_FLAG</Event_Type>" +
                      "<Event_Param1>MY_FLAG</Event_Param1>" +
                      "<Event_Param2>not_a_number</Event_Param2>" +
                      "<Event_Param3>EQUAL_TO</Event_Param3>" +
                      "<Reward_Type>ZOOM_IN</Reward_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("not_a_number"));
    }

    [Fact]
    public void PositiveInteger_invalid_value_produces_warning()
    {
        // STORY_ACCUMULATE Param1 is PositiveInteger required
        var sut = BuildCollector();
        var xml = Xml("<Event_Type>STORY_ACCUMULATE</Event_Type>" +
                      "<Event_Param1>-5</Event_Param1>" +
                      "<Reward_Type>ZOOM_IN</Reward_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("-5"));
    }

    // -----------------------------------------------------------------------
    // Ref kind validation
    // -----------------------------------------------------------------------

    [Fact]
    public void GameObjectTypeRef_resolved_in_index_produces_no_diagnostic()
    {
        // REMOVE_UNIT Reward_Param1 is GameObjectTypeRef required
        var sut = BuildCollector();
        var xml = Xml("<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                      "<Reward_Type>REMOVE_UNIT</Reward_Type>" +
                      "<Reward_Param1>X_Wing</Reward_Param1>");
        var index = IndexWithSymbol("X_Wing", "GameObjectType");

        var diags = sut.Collect(xml, index);

        Assert.DoesNotContain(diags, d => d.Message.Contains("X_Wing"));
    }

    [Fact]
    public void GameObjectTypeRef_not_in_index_produces_warning()
    {
        var sut = BuildCollector();
        var xml = Xml("<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                      "<Reward_Type>REMOVE_UNIT</Reward_Type>" +
                      "<Reward_Param1>Unknown_Unit</Reward_Param1>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Warning &&
                                     d.Message.Contains("Unknown_Unit"));
    }

    // -----------------------------------------------------------------------
    // Clean cases
    // -----------------------------------------------------------------------

    [Fact]
    public void STORY_MOVIE_DONE_with_no_params_produces_no_diagnostic()
    {
        var sut = BuildCollector();
        var xml = Xml("<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                      "<Reward_Type>ZOOM_IN</Reward_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Empty(diags);
    }

    [Fact]
    public void Unknown_event_type_produces_no_story_diagnostics()
    {
        // Unknown type is handled by DynamicEnumValueValidator; we must not double-report
        var sut = BuildCollector();
        var xml = Xml("<Event_Type>STORY_NOT_REAL</Event_Type>" +
                      "<Event_Param1>something</Event_Param1>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Empty(diags);
    }

    [Fact]
    public void Unknown_reward_type_produces_no_story_diagnostics()
    {
        var sut = BuildCollector();
        var xml = Xml("<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                      "<Reward_Type>NOT_A_REWARD</Reward_Type>" +
                      "<Reward_Param1>something</Reward_Param1>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        Assert.Empty(diags);
    }

    [Fact]
    public void Empty_param_value_is_not_validated()
    {
        // Empty values are treated as absent — no "wrong type" error, just potential "missing required"
        var sut = BuildCollector(FlagCompareMethod);
        var xml = Xml("<Event_Type>STORY_ACCUMULATE</Event_Type>" +
                      "<Event_Param1></Event_Param1>" +
                      "<Reward_Type>ZOOM_IN</Reward_Type>");

        var diags = sut.Collect(xml, GameIndex.Empty);

        // Missing required param warning only (not a "type" error on the empty value)
        Assert.All(diags, d => Assert.DoesNotContain("is not a valid", d.Message));
    }
}
