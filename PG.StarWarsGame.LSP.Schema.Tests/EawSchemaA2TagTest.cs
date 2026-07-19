// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Guards the A-2 schema curation changes: referenceType annotations added to the
///     confident batch of previously-untyped xmlObject tags.
///     Loads the real schema/eaw/ YAML files via SchemaIndex.
/// </summary>
public sealed class EawSchemaA2TagTest
{
    private static readonly SchemaIndex Schema = LoadEawSchemaIndex();

    // ── BinkMovie:Text_Crawl_Name → Draw3DTextCrawl ──────────────────────────

    [Fact]
    public void BinkMovie_TextCrawlName_ReferenceTypeIsDraw3DTextCrawl()
    {
        var tag = Schema.GetTagsForType("BinkMovie")
            .First(t => string.Equals(t.Tag, "Text_Crawl_Name", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("Draw3DTextCrawl", tag.ObjectType?.TypeName);
    }

    // ── BountyOnFactionAbility:Target_Faction_Names → Faction ────────────────

    [Fact]
    public void BountyOnFactionAbility_TargetFactionNames_ReferenceTypeIsFaction()
    {
        var tag = Schema.GetTagsForType("BountyOnFactionAbility")
            .First(t => string.Equals(t.Tag, "Target_Faction_Names", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("Faction", tag.ObjectType?.TypeName);
    }

    // ── CombatBonusAbility:Specific_Faction → Faction ────────────────────────

    [Fact]
    public void CombatBonusAbility_SpecificFaction_ReferenceTypeIsFaction()
    {
        var tag = Schema.GetTagsForType("CombatBonusAbility")
            .First(t => string.Equals(t.Tag, "Specific_Faction", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("Faction", tag.ObjectType?.TypeName);
    }

    // ── EnableAbilityAbility:Ability_Name → SpecialAbility ───────────────────

    [Fact]
    public void EnableAbilityAbility_AbilityName_ReferenceTypeIsSpecialAbility()
    {
        var tag = Schema.GetTagsForType("EnableAbilityAbility")
            .First(t => string.Equals(t.Tag, "Ability_Name", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("SpecialAbility", tag.ObjectType?.TypeName);
    }

    // ── Faction movie tags → BinkMovie ───────────────────────────────────────

    [Theory]
    [InlineData("Superweapon_Win_Movie")]
    [InlineData("Generic_Win_Movie")]
    [InlineData("Finale_Movie")]
    [InlineData("Finale_Movie_2")]
    [InlineData("Post_Credits_Movie")]
    [InlineData("Tactical_Intro_Command_Bar_Movie_Name")]
    public void Faction_MovieTag_ReferenceTypeIsBinkMovie(string tagName)
    {
        var tag = Schema.GetTagsForType("Faction")
            .First(t => string.Equals(t.Tag, tagName, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("BinkMovie", tag.ObjectType?.TypeName);
    }

    // ── Faction:Primary_Enemy → Faction ──────────────────────────────────────

    [Fact]
    public void Faction_PrimaryEnemy_ReferenceTypeIsFaction()
    {
        var tag = Schema.GetTagsForType("Faction")
            .First(t => string.Equals(t.Tag, "Primary_Enemy", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("Faction", tag.ObjectType?.TypeName);
    }

    // ── GameConstants radar SFX event tags → SFXEvent ────────────────────────

    [Theory]
    [InlineData("GUI_Attack_Movement_Click_Radar_Event_Name")]
    [InlineData("GUI_Movement_Click_Radar_Event_Name")]
    [InlineData("GUI_Movement_Double_Click_Radar_Event_Name")]
    public void GameConstants_RadarEventName_ReferenceTypeIsSFXEvent(string tagName)
    {
        var tag = Schema.GetTagsForType("GameConstants")
            .First(t => string.Equals(t.Tag, tagName, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("SFXEvent", tag.ObjectType?.TypeName);
    }

    // ── GameConstants faction side name tags → Faction ───────────────────────

    [Theory]
    [InlineData("Good_Side_Name")]
    [InlineData("Evil_Side_Name")]
    [InlineData("Corrupt_Side_Name")]
    public void GameConstants_SideName_ReferenceTypeIsFaction(string tagName)
    {
        var tag = Schema.GetTagsForType("GameConstants")
            .First(t => string.Equals(t.Tag, tagName, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("Faction", tag.ObjectType?.TypeName);
    }

    // ── GameConstants leader name tags → GameObjectType ──────────────────────

    [Theory]
    [InlineData("Good_Side_Leader_Name")]
    [InlineData("Evil_Side_Leader_Name")]
    [InlineData("Corrupt_Side_Leader_Name")]
    public void GameConstants_SideLeaderName_ReferenceTypeIsGameObjectType(string tagName)
    {
        var tag = Schema.GetTagsForType("GameConstants")
            .First(t => string.Equals(t.Tag, tagName, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("GameObjectType", tag.ObjectType?.TypeName);
    }

    // ── GameConstants:Raid_Force_Required_Faction → Faction ──────────────────

    [Fact]
    public void GameConstants_RaidForceRequiredFaction_ReferenceTypeIsFaction()
    {
        var tag = Schema.GetTagsForType("GameConstants")
            .First(t => string.Equals(t.Tag, "Raid_Force_Required_Faction", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("Faction", tag.ObjectType?.TypeName);
    }

    // ── GameObjectType light source tags → LightSource ───────────────────────

    [Theory]
    [InlineData("Explosion_Light_Source_Name")]
    [InlineData("Muzzle_Light_Source_Name")]
    public void GameObjectType_LightSourceName_ReferenceTypeIsLightSource(string tagName)
    {
        var tag = Schema.GetTagsForType("GameObjectType")
            .First(t => string.Equals(t.Tag, tagName, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("LightSource", tag.ObjectType?.TypeName);
    }

    // ── GameObjectType:Political_Faction → Faction ───────────────────────────

    [Fact]
    public void GameObjectType_PoliticalFaction_ReferenceTypeIsFaction()
    {
        var tag = Schema.GetTagsForType("GameObjectType")
            .First(t => string.Equals(t.Tag, "Political_Faction", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("Faction", tag.ObjectType?.TypeName);
    }

    // ── GameObjectType:Hero_Ability → SpecialAbility ─────────────────────────

    [Fact]
    public void GameObjectType_HeroAbility_ReferenceTypeIsSpecialAbility()
    {
        var tag = Schema.GetTagsForType("GameObjectType")
            .First(t => string.Equals(t.Tag, "Hero_Ability", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("SpecialAbility", tag.ObjectType?.TypeName);
    }

    // ── GameObjectType:GUI_Activated_Ability_Name → SpecialAbility ───────────

    [Fact]
    public void GameObjectType_GuiActivatedAbilityName_ReferenceTypeIsSpecialAbility()
    {
        var tag = Schema.GetTagsForType("GameObjectType")
            .First(t => string.Equals(t.Tag, "GUI_Activated_Ability_Name", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("SpecialAbility", tag.ObjectType?.TypeName);
    }

    // ── GameObjectType control transition radar events → SFXEvent ────────────

    [Theory]
    [InlineData("Begin_Control_Transition_Radar_Event")]
    [InlineData("End_Control_Transition_Radar_Event")]
    [InlineData("Beacon_Radar_Map_Event_Name")]
    public void GameObjectType_RadarEvent_ReferenceTypeIsSFXEvent(string tagName)
    {
        var tag = Schema.GetTagsForType("GameObjectType")
            .First(t => string.Equals(t.Tag, tagName, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("SFXEvent", tag.ObjectType?.TypeName);
    }

    // ── GameObjectType corruption bink hologram tags → BinkMovie ─────────────

    [Theory]
    [InlineData("Corruption_1_Success_Bink_Hologram_Name")]
    [InlineData("Corruption_1_Failure_Bink_Hologram_Name")]
    [InlineData("Corruption_2_Success_Bink_Hologram_Name")]
    [InlineData("Corruption_2_Failure_Bink_Hologram_Name")]
    [InlineData("Corruption_3_Success_Bink_Hologram_Name")]
    [InlineData("Corruption_3_Failure_Bink_Hologram_Name")]
    public void GameObjectType_CorruptionBinkHologramName_ReferenceTypeIsBinkMovie(string tagName)
    {
        var tag = Schema.GetTagsForType("GameObjectType")
            .First(t => string.Equals(t.Tag, tagName, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("BinkMovie", tag.ObjectType?.TypeName);
    }

    // ── engine enum wiring (2026-07-05: enumNames pointed at nonexistent enums) ─

    [Fact]
    public void GameObjectType_MovementClass_ResolvesMovementClassTypeEnum()
    {
        var tag = Schema.GetTagsForType("GameObjectType")
            .First(t => string.Equals(t.Tag, "MovementClass", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("MovementClassType", tag.Enum?.Name);
        Assert.Equal(EnumKind.DynamicXml, tag.Enum?.Kind);
    }

    [Fact]
    public void GameObjectType_SpaceLayer_ResolvesSchemaFixedSpaceLayerTypeEnum()
    {
        var tag = Schema.GetTagsForType("GameObjectType")
            .First(t => string.Equals(t.Tag, "Space_Layer", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("SpaceLayerType", tag.Enum?.Name);
        Assert.Equal(EnumKind.SchemaFixed, tag.Enum?.Kind);
        Assert.Contains(tag.Enum!.Values, v => v.Name == "SuperCapital");
    }

    [Fact]
    public void GameObjectType_UnitCollisionClass_ResolvesSchemaFixedCollisionClassTypeEnum()
    {
        var tag = Schema.GetTagsForType("GameObjectType")
            .First(t => string.Equals(t.Tag, "UnitCollisionClass", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("CollisionClassType", tag.Enum?.Name);
        Assert.Equal(EnumKind.SchemaFixed, tag.Enum?.Kind);
        // Engine collision classes contain spaces - must survive schema loading intact.
        Assert.Contains(tag.Enum!.Values, v => v.Name == "Landing Transport");
    }

    // ── HardPoint:Special_Ability_Name → deliberately UNVALIDATED ────────────

    [Fact]
    public void HardPoint_SpecialAbilityName_IsUnknownKind_NoFalseMissingObjectDiagnostics()
    {
        // The referenced ability lives on the object the hardpoint is ATTACHED to; validating it
        // needs an owner-object ability lookup (which unit mounts this hardpoint?) that is not
        // supported yet. Until then the tag stays referenceKind: unknown so no false "missing
        // object" diagnostics are emitted (2026-07-05 smoketest decision).
        var tag = Schema.GetTagsForType("HardPoint")
            .First(t => string.Equals(t.Tag, "Special_Ability_Name", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.Unknown, tag.ReferenceKind);
        Assert.Null(tag.ObjectType);
    }

    // ── RadioactiveContaminateAbility:Contamination_Object_Name → GameObjectType

    [Fact]
    public void RadioactiveContaminateAbility_ContaminationObjectName_ReferenceTypeIsGameObjectType()
    {
        var tag = Schema.GetTagsForType("RadioactiveContaminateAbility")
            .First(t => string.Equals(t.Tag, "Contamination_Object_Name", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("GameObjectType", tag.ObjectType?.TypeName);
    }

    // ── SaberThrowAbility:Saber_Name → GameObjectType ────────────────────────

    [Fact]
    public void SaberThrowAbility_SaberName_ReferenceTypeIsGameObjectType()
    {
        var tag = Schema.GetTagsForType("SaberThrowAbility")
            .First(t => string.Equals(t.Tag, "Saber_Name", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("GameObjectType", tag.ObjectType?.TypeName);
    }

    // ── SpecialAbilityAction:Ability_Name → SpecialAbility ───────────────────

    [Fact]
    public void SpecialAbilityAction_AbilityName_ReferenceTypeIsSpecialAbility()
    {
        var tag = Schema.GetTagsForType("SpecialAbilityAction")
            .First(t => string.Equals(t.Tag, "Ability_Name", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("SpecialAbility", tag.ObjectType?.TypeName);
    }

    // ── SpecialAbilityData:GUI_Activated_Ability_Name → SpecialAbility ───────

    [Fact]
    public void SpecialAbilityData_GuiActivatedAbilityName_ReferenceTypeIsSpecialAbility()
    {
        var tag = Schema.GetTagsForType("SpecialAbilityData")
            .First(t => string.Equals(t.Tag, "GUI_Activated_Ability_Name", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("SpecialAbility", tag.ObjectType?.TypeName);
    }

    // ── UnitAbility:GUI_Activated_Ability_Name → UnitAbility ─────────────────

    [Fact]
    public void UnitAbility_GuiActivatedAbilityName_ReferenceTypeIsUnitAbility()
    {
        var tag = Schema.GetTagsForType("UnitAbility")
            .First(t => string.Equals(t.Tag, "GUI_Activated_Ability_Name", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("UnitAbility", tag.ObjectType?.TypeName);
    }

    // ── schema loader (shared with EawSchemaA1TagTest) ────────────────────────

    private static SchemaIndex LoadEawSchemaIndex()
    {
        var root = FindSchemaRoot()
                   ?? throw new InvalidOperationException(
                       "schema/eaw/ not found - ensure the schema submodule is checked out.");

        var tagsByType = Directory
            .GetFiles(Path.Combine(root, "tags"), "*.yaml")
            .Select(f => (
                typeName: Path.GetFileNameWithoutExtension(f),
                tags: (IReadOnlyList<RawTagDefinition>)YamlSchemaParser.ParseTagFile(File.ReadAllText(f))))
            .ToList();

        var types = YamlSchemaParser.ParseTypeFile(
            File.ReadAllText(Path.Combine(root, "types.yaml")));

        var enums = Directory
            .GetFiles(Path.Combine(root, "enums"), "*.yaml")
            .Select(f => YamlSchemaParser.ParseEnumFile(File.ReadAllText(f)))
            .ToList();

        var hardcoded = Directory
            .GetFiles(Path.Combine(root, "hardcoded"), "*.yaml")
            .Select(f => YamlSchemaParser.ParseHardcodedSetFile(File.ReadAllText(f)))
            .ToList();

        return new SchemaIndex(tagsByType, types, enums, hardcoded);
    }

    private static string? FindSchemaRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(EawSchemaA2TagTest).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var candidate = Path.Combine(dir.FullName, "schema", "eaw");
                if (Directory.Exists(candidate)) return candidate;
                return null;
            }

            dir = dir.Parent;
        }

        return null;
    }
}