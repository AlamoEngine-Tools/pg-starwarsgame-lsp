// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Tags the engine requires to lie between 0 and 1, typed as <c>NormalizedFloat</c> rather than
///     plain <c>Float</c>.
/// </summary>
/// <remarks>
///     <para>
///         <b>This is a deliberate deviation from DatabaseMapExport.</b> The export types every one
///         of these as <c>Type="8"</c>, plain float, and so does the engine's own XML field table -
///         because the parser genuinely reads a float. The range is enforced later, when the ability
///         initialises, and the engine says so in its own words:
///     </para>
///     <para>
///         <c>Error: (%s) Absorb_Chance must be between 0 and 1.</c> (<c>01551084</c>),
///         <c>Damage_Absorb_Percentage</c> (<c>01551044</c>), <c>Damage_Percentage</c>
///         (<c>01551260</c>), <c>Block_Chance</c>, <c>Redirect_Chance</c>,
///         <c>Activation_Chance</c>, <c>Chance_To_Succeed</c>,
///         <c>Chance_To_Be_Caught_Upon_Failure</c> and <c>Chance_To_Win</c>.
///     </para>
///     <para>
///         So a tag/type diff against the export or the field table will report these as
///         mismatches. They are not. We are narrowing a float to the range the engine will demand
///         at runtime, which turns a message the modder sees only when the ability misbehaves into
///         one they see while typing. The same judgement as typing <c>Armor_Type</c> as
///         <c>DynamicEnumValue</c> where the engine reads a bare name.
///     </para>
///     <para>
///         Measured against shipped data before applying: every value these tags carry in either
///         game already lies within 0..1. That is context, not the criterion. Vanilla data raises
///         assertions of its own on startup, so a rule the shipped files break is still a rule -
///         flagging it agrees with the engine rather than contradicting it. The engine's message is
///         the authority here; the corpus only says how loud the change would be.
///     </para>
/// </remarks>
public sealed class EawSchemaNormalizedChanceTagTest
{
    // file, tag - every occurrence, since several of these appear on more than one ability type.
    public static TheoryData<string, string> Cases =>
        new()
        {
            { "AbsorbBlasterAbility.yaml", "Absorb_Chance" },
            { "AbsorbBlasterAbility.yaml", "Damage_Absorb_Percentage" },
            { "RedirectBlasterAbility.yaml", "Block_Chance" },
            { "RedirectBlasterAbility.yaml", "Redirect_Chance" },
            { "HeroAssassinAbility.yaml", "Chance_To_Succeed" },
            { "HeroAssassinAbility.yaml", "Chance_To_Be_Caught_Upon_Failure" },
            { "PlanetIncomeGamblingAbility.yaml", "Chance_To_Win" },
            { "ArcSweepAttackAbility.yaml", "Activation_Chance" },
            { "ArcSweepAttackAbility.yaml", "Damage_Percentage" },
            { "CableAttackAbility.yaml", "Activation_Chance" },
            { "DemolitionAbility.yaml", "Activation_Chance" },
            { "DemolitionAbility.yaml", "Damage_Percentage" },
            { "EarthquakeAttackAbility.yaml", "Activation_Chance" },
            { "EarthquakeAttackAbility.yaml", "Damage_Percentage" },
            { "EatAttackAbility.yaml", "Activation_Chance" },
            { "EatAttackAbility.yaml", "Damage_Percentage" },
            { "ForceTelekinesisAbility.yaml", "Activation_Chance" },
            { "ForceTelekinesisAbility.yaml", "Damage_Percentage" },
            { "GenericAttackAbility.yaml", "Damage_Percentage" },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ChanceAndPercentageTags_AreNormalized(string file, string tag)
    {
        Assert.Equal(XmlValueType.NormalizedFloat, Tag(file, tag).ValueType);
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
            Path.GetDirectoryName(typeof(EawSchemaNormalizedChanceTagTest).Assembly.Location)!);
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
