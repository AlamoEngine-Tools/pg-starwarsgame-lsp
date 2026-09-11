// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Guards that every ability element authored inside an <c>&lt;Abilities&gt;</c> sub-object list in
///     vanilla data resolves to a registered object type carrying a <c>Name</c> name-tag.
///     <para>
///         The parser scopes an ability to its owner only when the ability element's PascalCase name
///         resolves via <c>SchemaIndex.GetObjectType</c> to a type with a name-tag; otherwise the
///         element is silently skipped and the ability is never indexed. A reference to that ability -
///         for example a hardpoint's <c>Special_Ability_Name</c> - then fails to resolve and is
///         reported as a false-positive unresolved reference.
///     </para>
///     <para>
///         Caught 2026-07-19: <c>Base_Power_Ability</c> (and seven siblings) were used in vanilla but
///         had no registered object type, so <c>Underworld_Palace_Base_Power</c> on
///         <c>HP_PALACE_POWER_GEN</c> was flagged. The element names below are the full set found by
///         sweeping every <c>&lt;Abilities&gt;</c> block in the base game.
///     </para>
/// </summary>
public sealed class AbilitySubObjectTypeRegistrationTest
{
    private static readonly SchemaIndex Schema = LoadEawSchemaIndex();

    [Theory]
    [InlineData("Base_Power_Ability")]
    [InlineData("Cable_Ability")]
    [InlineData("Enable_Ability")]
    [InlineData("Hack_Super_Weapon_Ability")]
    [InlineData("Planet_Destruction_Ability")]
    [InlineData("Political_Control_Protection_Ability")]
    [InlineData("Retreat_Prevention_Ability")]
    [InlineData("Starbase_Upgrade_Ability")]
    public void VanillaAbilityElement_ResolvesToRegisteredObjectTypeWithNameTag(string elementName)
    {
        // Mirror the parser's element-name -> type-name conversion (XmlUtility.ToPascalCase).
        var typeName = string.Concat(elementName.Split('_')
            .Select(w => w.Length == 0 ? "" : char.ToUpperInvariant(w[0]) + w[1..]));

        var type = Schema.GetObjectType(typeName);

        Assert.True(type is not null,
            $"<{elementName}> maps to object type '{typeName}', which is not registered in types.yaml. "
            + "The ability will not be indexed and references to it are false-positive unresolved.");
        Assert.Equal("Name", type!.NameTag);
    }

    /// <summary>
    ///     Resolving to a registered TYPE is not enough - the type has to reach its own TAGS.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>SchemaIndex</c> keys a tag file's contents under the file's BASENAME, and the
    ///         resolver asks for them by owning type name. So a file named after something that is
    ///         not the type an element resolves to is unreachable through any resolution context.
    ///     </para>
    ///     <para>
    ///         Caught 2026-09-12: <c>&lt;Enable_Ability&gt;</c> resolves to <c>EnableAbility</c>,
    ///         but its tags lived in <c>EnableAbilityAbility.yaml</c> under a second, phantom type
    ///         that no element maps to. Both of its tags happen to exist on other types, so the flat
    ///         fallback still resolved them - with the OTHER type's definitions. Which
    ///         <c>Ability_Name</c> won was decided by file load order, and this ability is used in
    ///         <c>Upgradeobjects.xml</c> in both games.
    ///     </para>
    /// </remarks>
    [Theory]
    [InlineData("Enable_Ability", "Ability_Name")]
    [InlineData("Enable_Ability", "Affects_All_Allies")]
    public void VanillaAbilityElement_ReachesItsOwnTags(string elementName, string tagName)
    {
        var typeName = string.Concat(elementName.Split('_')
            .Select(w => w.Length == 0 ? "" : char.ToUpperInvariant(w[0]) + w[1..]));

        var tags = Schema.GetTagsForType(typeName);

        Assert.True(tags.Any(t => string.Equals(t.Tag, tagName, StringComparison.OrdinalIgnoreCase)),
            $"<{elementName}> resolves to type '{typeName}', which does not declare '{tagName}'. "
            + $"The tag file is probably not named '{typeName}.yaml', so the tags are only reachable "
            + "through the global fallback - with whatever definition another type happens to give them.");
    }

    /// <summary>
    ///     <c>Ability_Name</c> means different things on different owners, so the owner's own
    ///     definition has to be the one that wins.
    /// </summary>
    /// <remarks>
    ///     On <c>Enable_Ability</c> it points at a <c>SpecialAbility</c>; on
    ///     <c>SpecialAbilityAction</c> it is declared differently. With the tags unreachable, the
    ///     flat lookup handed out whichever definition loaded first - so go-to-definition and
    ///     reference validation were resolving against the wrong target.
    /// </remarks>
    [Fact]
    public void EnableAbility_AbilityName_TargetsASpecialAbility()
    {
        var tag = Schema.GetTagsForType("EnableAbility")
            .FirstOrDefault(t => t.Tag.Equals("Ability_Name", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(tag);
        Assert.Equal(ReferenceKind.XmlObject, tag!.ReferenceKind);
        Assert.Equal("SpecialAbility", tag.ReferenceTypeName);
    }

    // The phantom half of the same bug: a registered type no XML element can ever resolve to.
    [Fact]
    public void No_phantom_EnableAbilityAbility_type_remains()
    {
        Assert.Null(Schema.GetObjectType("EnableAbilityAbility"));
    }

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
            Path.GetDirectoryName(typeof(AbilitySubObjectTypeRegistrationTest).Assembly.Location)!);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "schema", "eaw");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
