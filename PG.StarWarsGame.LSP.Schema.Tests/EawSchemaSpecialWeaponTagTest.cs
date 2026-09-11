// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     The faction special-weapon tags (issues #98 and #99).
/// </summary>
/// <remarks>
///     <para>
///         <c>_B</c> is deprecated because the engine supports only one special weapon, and vanilla
///         leaves it empty in every faction that declares it. That deprecation IS the diagnostic
///         #99 asked for - it fires through <c>DeprecatedTagHandler</c>, and now carries the
///         schema's reason with it - so the tag must keep both the flag and a description to
///         explain itself.
///     </para>
///     <para>
///         <c>_A</c> deliberately has NO name restriction. The issue text asked to enforce
///         "Hypervelocity Cannon or Ion Cannon", but shipped data names
///         <c>Ground_Ion_Cannon</c> and <c>Ground_Empire_Hypervelocity_Gun</c>, so that rule would
///         have fired on vanilla itself - and the reporter withdrew it in the comments for the same
///         reason (EaWX uses the <c>Ground_</c> prefix, other mods use their own names). This pins
///         the absence so the whitelist does not come back.
///     </para>
/// </remarks>
public sealed class EawSchemaSpecialWeaponTagTest
{
    private static readonly IReadOnlyList<RawTagDefinition> FactionTags = LoadTags("Faction.yaml");

    [Fact]
    public void WeaponB_StaysDeprecated_AndSaysWhy()
    {
        var tag = Tag("Standalone_Space_Maps_Special_Weapon_B");

        Assert.True(tag.Deprecated, "the engine supports only one special weapon");
        Assert.True(tag.Description.TryGetValue("en", out var reason) && reason.Length > 0,
            "a deprecation the reader cannot explain is not actionable - the handler surfaces this text");
    }

    [Fact]
    public void WeaponA_IsAPlainObjectReference_WithNoNameWhitelist()
    {
        var tag = Tag("Standalone_Space_Maps_Special_Weapon_A");

        Assert.Equal(ReferenceKind.XmlObject, tag.ReferenceKind);
        Assert.Equal("GameObjectType", tag.ReferenceType);
        Assert.False(tag.Deprecated);

        // Vanilla's own values would fail a Hypervelocity/Ion name check.
        var text = tag.Description.GetValueOrDefault("en", string.Empty);
        Assert.DoesNotContain("Hypervelocity", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ion Cannon", text, StringComparison.OrdinalIgnoreCase);
    }

    private static RawTagDefinition Tag(string name)
    {
        var tag = FactionTags.FirstOrDefault(t =>
            string.Equals(t.Tag, name, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(tag);
        return tag!;
    }

    private static IReadOnlyList<RawTagDefinition> LoadTags(string tagFile)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(EawSchemaSpecialWeaponTagTest).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var candidate = Path.Combine(dir.FullName, "schema", "eaw", "tags", tagFile);
                if (File.Exists(candidate))
                    return YamlSchemaParser.ParseTagFile(File.ReadAllText(candidate));
                break;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"schema/eaw/tags/{tagFile} not found.");
    }
}
