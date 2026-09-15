// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     The three ability classes that refuse to run without despawning their owner.
/// </summary>
/// <remarks>
///     <para>
///         Each states it in its own <c>Validate_Data</c> and then sets the flag to true itself:
///         <c>GalacticSabotageAbilityClass</c> (<c>00ef6c30</c>), <c>BlackMarketAbilityClass</c>
///         (<c>00f21130</c>) and <c>SlicerAbilityClass</c> (<c>00f1e8b0</c>). Writing
///         <c>No</c> does not disable the despawn - it only hides it from the file.
///     </para>
///     <para>
///         Declared as <c>allowedValues</c> on the owner rather than as a rule, because the
///         restriction belongs to the tag on that type. It is the same shape as the
///         <c>Activation_Style</c> narrowing each of these three also carries, and it is the
///         opposite of <c>AutomaticAbilityDespawnRule</c> without colliding with it: all three
///         accept only <c>Ground_Activated</c>, which is not an automatic style.
///     </para>
/// </remarks>
public sealed class EawSchemaCausesDespawnTest
{
    [Theory]
    [InlineData("GalacticSabotageAbility.yaml")]
    [InlineData("BlackMarketAbility.yaml")]
    [InlineData("SlicerAbility.yaml")]
    public void Ability_requires_causes_despawn(string file)
    {
        var tag = Tag(file, "Causes_Despawn");

        Assert.Equal(["Yes"], tag.AllowedValues);
    }

    /// <summary>
    ///     The narrowing must not be mistaken for a type change - the engine still parses a plain
    ///     boolean, and Yes/True/1 stay interchangeable.
    /// </summary>
    [Theory]
    [InlineData("GalacticSabotageAbility.yaml")]
    [InlineData("BlackMarketAbility.yaml")]
    [InlineData("SlicerAbility.yaml")]
    public void The_tag_is_still_a_boolean(string file)
    {
        Assert.Equal(XmlValueType.Boolean, Tag(file, "Causes_Despawn").ValueType);
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
            Path.GetDirectoryName(typeof(EawSchemaCausesDespawnTest).Assembly.Location)!);
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