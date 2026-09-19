// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     A category mask holds several categories OR-ed together, and the schema says so with
///     <c>semanticType: FlagList</c> (issue #123).
/// </summary>
/// <remarks>
///     <para>
///         FlagList is the only thing that lets <c>DynamicEnumValueHandler</c> accept a '|': without
///         it the value is read as one identifier and the author is told "expects a single enum
///         identifier; '|' is not allowed here", which is the error the issue reports. Commas are
///         split either way, so this is purely about the OR operator.
///     </para>
///     <para>
///         The shipped corpus cannot settle <c>Excluded_Unit_Categories</c> - it appears in neither
///         eaw/ nor foc/. The evidence is its twin instead: <c>Applicable_Unit_Categories</c> sits
///         beside it on the same ability, binds the same enum, carries the same engine parameter type
///         (14 = 0x0e in DatabaseMapExport.xml) and ships '|' expressions 116 times. Two tags that are
///         the same shape must be typed the same way, which is what the first test pins - naming the
///         pair rather than the literal, so the invariant survives a rename.
///     </para>
///     <para>
///         Measured across both corpora: every tag that ships a '|' value is already marked FlagList
///         (15 of 15), so this is a single omission rather than a class of them.
///     </para>
/// </remarks>
public sealed class EawSchemaCategoryMaskTagTest
{
    [Fact]
    public void ApplicableAndExcludedUnitCategories_AreTypedTheSame()
    {
        var applicable = Tag("Applicable_Unit_Categories");
        var excluded = Tag("Excluded_Unit_Categories");

        Assert.Equal(applicable.ValueType, excluded.ValueType);
        Assert.Equal(applicable.EnumName, excluded.EnumName);
        Assert.Equal(applicable.SemanticType, excluded.SemanticType);
    }

    [Fact]
    public void ExcludedUnitCategories_IsAFlagList()
    {
        Assert.Equal(TagSemanticType.FlagList, Tag("Excluded_Unit_Categories").SemanticType);
    }

    [Fact]
    public void ExcludedUnitCategories_StillBindsTheCategoryEnum()
    {
        // FlagList changes how the value is SPLIT, never what the parts must be.
        var tag = Tag("Excluded_Unit_Categories");

        Assert.Equal(ReferenceKind.Enum, tag.ReferenceKind);
        Assert.Equal("GameObjectCategoryType", tag.EnumName);
    }

    /// <remarks>
    ///     Read out of the 2018 build's tag table, where each row is
    ///     {name, type code, struct offset, descriptor}. Every one of these carries type <c>0x0e</c>
    ///     and the same enum descriptor <c>0x018ead3c</c> as <c>Applicable_Unit_Categories</c>, the
    ///     known mask - so the engine parses them all through one path and accepts '|' in each.
    ///     The table discriminates, which is what makes that meaningful: <c>Fire_Category_Restrictions</c>
    ///     is type <c>0x2a</c> with NO descriptor and <c>Unit_Command_Rankings_By_Category</c> is
    ///     <c>0x1b</c>, and both are correctly left unmarked.
    ///     <para>
    ///         <c>Garrison_Category</c> is here because the corpus misled: it ships comma-separated
    ///         values, commas are split whatever the tag's type, so it looked fine. What vanilla
    ///         happens to write is not what the engine accepts.
    ///     </para>
    ///     <para>
    ///         <c>Space_Or_Garrison_Category</c> is the weak one. Its schema entry calls it an
    ///         alternate spelling of <c>Garrison_Category</c>, but it appears in NO tag-table row in
    ///         the 2018 build, in neither game's DatabaseMapExport.xml, and nowhere in either corpus.
    ///         Marking it costs nothing - FlagList only ever widens what is accepted - but the mark is
    ///         not evidence that the tag exists, and the alternate-spelling claim is still unverified.
    ///         An absence in that build is never proof on its own; it predates the shipped game.
    ///     </para>
    /// </remarks>
    [Theory]
    [InlineData("Planet_Restricted_Unit_Categories")]
    [InlineData("Garrison_Category")]
    [InlineData("Space_Or_Garrison_Category")]
    public void CategoryMaskTagsOnGameObjectType_AreFlagLists(string tagName)
    {
        Assert.Equal(TagSemanticType.FlagList, GameObjectTag(tagName).SemanticType);
    }

    [Theory]
    [InlineData("Fire_Category_Restrictions")]
    [InlineData("Unit_Command_Rankings_By_Category")]
    public void TagsTheEngineParsesDifferently_AreNotFlagLists(string tagName)
    {
        // The control for the theory above. These two bind the same enum in our schema but carry a
        // different type code and no enum descriptor in the engine, so they are not masks.
        Assert.NotEqual(TagSemanticType.FlagList, GameObjectTag(tagName).SemanticType);
    }

    private static RawTagDefinition GameObjectTag(string name)
    {
        var file = name is "Unit_Command_Rankings_By_Category" ? "GameConstants.yaml"
            : name is "Fire_Category_Restrictions" ? "HardPoint.yaml"
            : "GameObjectType.yaml";
        var tags = YamlSchemaParser.ParseTagFile(File.ReadAllText(Find("tags", file)));
        var tag = tags.FirstOrDefault(t => string.Equals(t.Tag, name, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(tag);
        return tag!;
    }

    private static RawTagDefinition Tag(string name)
    {
        var tags = YamlSchemaParser.ParseTagFile(File.ReadAllText(Find("tags", "SpecialAbility.yaml")));
        var tag = tags.FirstOrDefault(t => string.Equals(t.Tag, name, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(tag);
        return tag!;
    }

    private static string Find(string folder, string file)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(EawSchemaCategoryMaskTagTest).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var candidate = Path.Combine(dir.FullName, "schema", "eaw", folder, file);
                if (File.Exists(candidate)) return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate schema/eaw/{folder}/{file}");
    }
}