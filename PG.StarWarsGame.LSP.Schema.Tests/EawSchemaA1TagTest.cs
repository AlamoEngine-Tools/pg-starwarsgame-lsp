// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Guards the A-1 schema curation changes against regression.
///     Loads the real schema/eaw/ YAML files and asserts per-tag properties.
/// </summary>
public sealed class EawSchemaA1TagTest
{
    private static readonly SchemaIndex Schema = LoadEawSchemaIndex();

    // ── BlackMarketItem:Name ─────────────────────────────────────────────────

    [Fact]
    public void BlackMarketItem_Name_ReferenceKindIsNotXmlObject()
    {
        var tag = Schema.GetTagsForType("BlackMarketItem")
            .First(t => string.Equals(t.Tag, "Name", StringComparison.OrdinalIgnoreCase));

        Assert.NotEqual(ReferenceKind.XmlObject, tag.ReferenceKind);
    }

    // ── GameObjectType:Planet_Ability_Name ───────────────────────────────────

    [Fact]
    public void GameObjectType_PlanetAbilityName_ReferenceKindIsLocalisationKey()
    {
        var tag = Schema.GetTagsForType("GameObjectType")
            .First(t => string.Equals(t.Tag, "Planet_Ability_Name", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.LocalisationKey, tag.ReferenceKind);
    }

    // ── GameObjectType:Primary_Locomotor_Name ────────────────────────────────

    [Fact]
    public void GameObjectType_PrimaryLocomotorName_IsHardcodedSetBehaviorModuleWithLocomotorGroup()
    {
        var tag = Schema.GetTagsForType("GameObjectType")
            .First(t => string.Equals(t.Tag, "Primary_Locomotor_Name", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.HardcodedSet, tag.ReferenceKind);
        Assert.NotNull(tag.HardcodedSet);
        Assert.Equal("BehaviorModule", tag.HardcodedSet.Name, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["Locomotor"], tag.ValueGroups);
    }

    [Fact]
    public void GameObjectType_SecondaryLocomotorName_IsHardcodedSetBehaviorModuleWithLocomotorGroup()
    {
        var tag = Schema.GetTagsForType("GameObjectType")
            .First(t => string.Equals(t.Tag, "Secondary_Locomotor_Name", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ReferenceKind.HardcodedSet, tag.ReferenceKind);
        Assert.NotNull(tag.HardcodedSet);
        Assert.Equal("BehaviorModule", tag.HardcodedSet.Name, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["Locomotor"], tag.ValueGroups);
    }

    // ── BehaviorModule locomotor group membership ────────────────────────────

    [Fact]
    public void BehaviorModule_WalkLocomotor_HasLocomotorGroup()
    {
        var set = Schema.AllHardcodedSets
            .First(s => string.Equals(s.Name, "BehaviorModule", StringComparison.OrdinalIgnoreCase));

        var value = set.Values.First(v => string.Equals(v.Name, "WALK_LOCOMOTOR", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("Locomotor", value.Groups, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BehaviorModule_JetpackLocomotor_ExistsAndHasLocomotorGroup()
    {
        var set = Schema.AllHardcodedSets
            .First(s => string.Equals(s.Name, "BehaviorModule", StringComparison.OrdinalIgnoreCase));

        var value = set.Values.FirstOrDefault(v =>
            string.Equals(v.Name, "JETPACK_LOCOMOTOR", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(value);
        Assert.Contains("Locomotor", value.Groups, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BehaviorModule_AbilityCountdown_DoesNotHaveLocomotorGroup()
    {
        var set = Schema.AllHardcodedSets
            .First(s => string.Equals(s.Name, "BehaviorModule", StringComparison.OrdinalIgnoreCase));

        var value =
            set.Values.First(v => string.Equals(v.Name, "ABILITY_COUNTDOWN", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain("Locomotor", value.Groups, StringComparer.OrdinalIgnoreCase);
    }

    // ── schema loader ────────────────────────────────────────────────────────

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
            Path.GetDirectoryName(typeof(EawSchemaA1TagTest).Assembly.Location)!);
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