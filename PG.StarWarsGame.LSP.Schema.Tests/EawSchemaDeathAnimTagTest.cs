// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     <c>Specific_Death_Anim_Type</c> (issue #104) is validated by the enum machinery, not by a
///     handler of its own.
/// </summary>
/// <remarks>
///     <para>
///         The issue asks to enforce "animation type must exist". The value half of that already
///         works: the tag is a <c>DynamicEnumValue</c> bound to <c>AnimationType</c>, so an unknown
///         value is reported by <c>DynamicEnumValueHandler</c>. Measured against shipped data, the
///         nine distinct values vanilla uses - FW_DIE, FL_DIE, Crushed, DIE, FIRE_DIE, FTK_DIE,
///         CA_DIE, DEPLOYED_DIE, DEPLOYED_CA_DIE - are all present in the enum, so it neither
///         misses nor false-positives today.
///     </para>
///     <para>
///         The description used to give <c>DEPLOY_DIE</c> as an example. No such value exists in the
///         enum or anywhere in shipped data; the real one is <c>DEPLOYED_DIE</c>, used twice. A
///         wrong example in a hover is worse than none, because it reads as permission.
///     </para>
///     <para>
///         What is NOT covered is whether the unit's MODEL actually carries the clip - the same
///         reach <c>DamageStageNotOnModelHandler</c> has for damage staging. That needs a
///         model-animation index, which does not exist yet.
///     </para>
/// </remarks>
public sealed class EawSchemaDeathAnimTagTest
{
    [Fact]
    public void DeathAnimType_IsBoundToTheAnimationTypeEnum()
    {
        var tag = Tag("Specific_Death_Anim_Type");

        Assert.Equal(ReferenceKind.Enum, tag.ReferenceKind);
        Assert.Equal("AnimationType", tag.EnumName);
    }

    // Every example the description offers must be a value the enum accepts, or the hover is
    // telling the reader to write something we will then flag.
    [Theory]
    [InlineData("DIE")]
    [InlineData("FW_DIE")]
    [InlineData("DEPLOYED_DIE")]
    public void ExamplesInTheDescription_AreRealAnimationTypes(string example)
    {
        var text = Tag("Specific_Death_Anim_Type").Description.GetValueOrDefault("en", string.Empty);
        Assert.Contains(example, text, StringComparison.Ordinal);

        var animationTypes = LoadEnumValues("AnimationType.yaml");
        Assert.Contains(example, animationTypes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheOldTypo_IsGone()
    {
        var text = Tag("Specific_Death_Anim_Type").Description.GetValueOrDefault("en", string.Empty);

        // DEPLOY_DIE does not exist; DEPLOYED_DIE does. Matching on the underscore keeps this from
        // passing merely because DEPLOYED_DIE contains the shorter string.
        Assert.DoesNotContain("DEPLOY_DIE", text, StringComparison.Ordinal);
    }

    // The other half of #104. The engine's own XML field table registers Specific_Death_Anim_Index
    // with type code 0x05, which is UInt - the same code Squadron_Capacity carries, and the 2006
    // map-editor export agrees at Type="5". We had it as Float, which offers a decimal where the
    // engine reads an unsigned index.
    [Fact]
    public void DeathAnimIndex_IsAnUnsignedIndex_NotAFloat()
    {
        Assert.Equal(XmlValueType.UInt, Tag("Specific_Death_Anim_Index").ValueType);
    }

    private static RawTagDefinition Tag(string name)
    {
        var tags = YamlSchemaParser.ParseTagFile(File.ReadAllText(Find("tags", "GameObjectType.yaml")));
        var tag = tags.FirstOrDefault(t => string.Equals(t.Tag, name, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(tag);
        return tag!;
    }

    private static IReadOnlyList<string> LoadEnumValues(string enumFile)
    {
        return YamlSchemaParser.ParseEnumFile(File.ReadAllText(Find("enums", enumFile)))
            .Values.Select(v => v.Name).ToList();
    }

    private static string Find(string folder, string file)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(EawSchemaDeathAnimTagTest).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var candidate = Path.Combine(dir.FullName, "schema", "eaw", folder, file);
                if (File.Exists(candidate)) return candidate;
                break;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"schema/eaw/{folder}/{file} not found.");
    }
}
