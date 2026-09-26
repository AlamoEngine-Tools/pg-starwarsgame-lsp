// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Pins the Campaign tags to what the engine does with them, measured against the campaign
///     field table and the consumers of each getter. Loads the real schema/eaw/ YAML files.
/// </summary>
public sealed class EawSchemaCampaignEngineTest
{
    private static readonly IReadOnlyList<RawTagDefinition> CampaignTags =
        YamlSchemaParser.ParseTagFile(EawSchemaRepo.Read("tags/Campaign.yaml"));

    private static readonly RawEnumDefinition VictoryConditions =
        YamlSchemaParser.ParseEnumFile(EawSchemaRepo.Read("enums/GalacticVictoryCondition.yaml"));

    // The engine's victory converter registers eleven Galactic_* names. Credits_Accrued and
    // Percentage_Control were missing, although their parameter tags (_Credit_Target,
    // _Galactic_Control) were already declared.
    [Theory]
    [InlineData("GALACTIC_CREDITS_ACCRUED")]
    [InlineData("GALACTIC_PERCENTAGE_CONTROL")]
    public void GalacticVictoryCondition_HasEveryEngineMember(string member)
    {
        Assert.Contains(VictoryConditions.Values, v => v.Name == member);
    }

    [Fact]
    public void GalacticVictoryCondition_HasElevenMembers()
    {
        Assert.Equal(11, VictoryConditions.Values.Count);
    }

    // Starting_Active_Player is looked up as a faction; it decides which member of a Campaign_Set
    // the chosen faction plays. The victory name lists resolve to a faction, any object type
    // (leaders) and a planet respectively.
    [Theory]
    [InlineData("Starting_Active_Player", "Faction")]
    [InlineData("Human_Victory_Faction_Names", "Faction")]
    [InlineData("AI_Victory_Faction_Names", "Faction")]
    [InlineData("Human_Victory_Leader_Names", "GameObjectType")]
    [InlineData("AI_Victory_Leader_Names", "GameObjectType")]
    [InlineData("Human_Victory_Planet_Names", "Planet")]
    [InlineData("AI_Victory_Planet_Names", "Planet")]
    [InlineData("Autoresolve_Exclusion_Locations", "Planet")]
    public void CampaignTag_ReferencesWhatTheEngineLooksUp(string tagName, string referenceType)
    {
        var tag = Tag(tagName);

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal(referenceType, tag.ReferenceType);
    }

    // Same reader as Starting_Credits, but the only consumer looks slot 0 up as a planet.
    [Fact]
    public void CorruptionLevelOverride_IsAPlanetValuePair()
    {
        var tag = Tag("Corruption_Level_Override");

        Assert.Equal(XmlValueType.PerFactionValue, tag.ValueType);
        Assert.Equal(TagSemanticType.PlanetValuePair, tag.SemanticType);
        Assert.Equal("Planet", tag.ReferenceType);
    }

    // The exclusion compares its mode slot with the literals land and space, nothing else.
    [Fact]
    public void AutoresolveExclusionMode_IsLandAndSpace()
    {
        var modes = YamlSchemaParser.ParseEnumFile(EawSchemaRepo.Read("enums/AutoresolveExclusionMode.yaml"));

        Assert.Equal(["LAND", "SPACE"], modes.Values.Select(v => v.Name).Order());
    }

    // Tags whose engine behaviour is not what the name suggests. Each carries a description so the
    // hover says it; an empty one here is how the wrong assumptions went unnoticed.
    [Theory]
    [InlineData("Tutorial")]
    [InlineData("Is_Listed")]
    [InlineData("Is_Story_Campaign")]
    [InlineData("Is_Autoresolve_Allowed")]
    [InlineData("Starting_Active_Player")]
    [InlineData("Special_Case_Production")]
    [InlineData("Corruption_Level_Override")]
    [InlineData("Starting_Credits")]
    [InlineData("Human_Victory_Galactic_Control")]
    [InlineData("Human_Victory_Cycle_Limit")]
    public void CampaignTag_HasADescription(string tagName)
    {
        var tag = Tag(tagName);

        Assert.True(tag.Description.TryGetValue("en", out var text) && !string.IsNullOrWhiteSpace(text),
            $"{tagName} has no description.");
    }

    private static RawTagDefinition Tag(string tagName)
    {
        var tag = CampaignTags.FirstOrDefault(t =>
            string.Equals(t.Tag, tagName, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(tag);
        return tag;
    }
}