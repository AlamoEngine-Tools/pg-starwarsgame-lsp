// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Nine ability classes accept exactly one <c>Activation_Style</c> and overwrite anything else.
/// </summary>
/// <remarks>
///     <para>
///         The shape, from <c>ReduceProductionTimeAbilityClass::Validate_Data</c>
///         (<c>010180b0</c>): <c>if (style != 3) { Message_Popup("The only currently supported
///         Activation_Style is Galactic_Automatic."); style = 3; }</c>. The object still loads, so
///         the ability runs under a trigger the author did not write and nothing on screen says so.
///     </para>
///     <para>
///         Declared as <c>allowedValues</c> on each owner rather than as narrower enums: the shared
///         nine-value enum stays correct everywhere else <c>Activation_Style</c> appears, and the
///         restriction is a property of the ability class.
///     </para>
/// </remarks>
public sealed class EawSchemaActivationStyleTest
{
    [Theory]
    [InlineData("HeroAssassinAbility.yaml", "COMBAT_IMMINENT")]
    [InlineData("RetreatProtectionAbility.yaml", "GALACTIC_AUTOMATIC")]
    [InlineData("ReduceTechnologyPriceAbility.yaml", "GALACTIC_AUTOMATIC")]
    [InlineData("ReduceProductionTimeAbility.yaml", "GALACTIC_AUTOMATIC")]
    [InlineData("ReduceProductionPriceAbility.yaml", "GALACTIC_AUTOMATIC")]
    [InlineData("GalaxyWideUpgradeAbility.yaml", "GALACTIC_AUTOMATIC")]
    [InlineData("GalacticSabotageAbility.yaml", "GROUND_ACTIVATED")]
    [InlineData("BlackMarketAbility.yaml", "GROUND_ACTIVATED")]
    [InlineData("SlicerAbility.yaml", "GROUND_ACTIVATED")]
    // These two declare NO tags of their own - both Map_Derived_Class_Member bodies are empty - so
    // their files exist purely to narrow this one inherited tag. Neither ability appears in the
    // shipped corpus.
    [InlineData("HackSuperWeaponAbility.yaml", "GROUND_ACTIVATED")]
    [InlineData("EliminateHeroAbility.yaml", "HERO_DETECTED")]
    public void Ability_allows_only_the_style_its_validator_names(string file, string style)
    {
        var tag = Tag(file, "Activation_Style");

        Assert.Equal([style], tag.AllowedValues);
        Assert.Equal("SpecialAbilityActivationStyle", tag.EnumName);
    }

    /// <summary>
    ///     The two values only the engine's messages attest to.
    /// </summary>
    /// <remarks>
    ///     Neither appears anywhere in the shipped corpus, because neither owning ability class
    ///     does. They are in the enum on the strength of the engine naming them as the supported
    ///     style - without them, writing what the engine asks for reads as an unknown enum member.
    /// </remarks>
    [Theory]
    [InlineData("COMBAT_IMMINENT")]
    [InlineData("HERO_DETECTED")]
    public void Engine_named_styles_are_in_the_enum(string value)
    {
        var path = Path.Combine(Path.GetDirectoryName(Find("GameObjectType.yaml"))!, "..", "enums",
            "SpecialAbilityActivationStyle.yaml");
        var parsed = YamlSchemaParser.ParseEnumFile(File.ReadAllText(Path.GetFullPath(path)));

        Assert.Contains(parsed.Values, v => v.Name == value);
    }

    private static RawTagDefinition Tag(string file, string name)
    {
        var tags = YamlSchemaParser.ParseTagFile(File.ReadAllText(Find(file)));
        var tag = tags.FirstOrDefault(t => string.Equals(t.Tag, name, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(tag);
        return tag!;
    }

    private static string Find(string file)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(EawSchemaActivationStyleTest).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var candidate = Path.Combine(dir.FullName, "schema", "eaw", "tags", file);
                if (File.Exists(candidate)) return candidate;
                break;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"schema/eaw/tags/{file} not found.");
    }
}
