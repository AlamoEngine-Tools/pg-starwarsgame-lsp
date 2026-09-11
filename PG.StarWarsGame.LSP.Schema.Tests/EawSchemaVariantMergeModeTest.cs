// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     The tags a variant APPENDS to its base rather than replacing.
/// </summary>
/// <remarks>
///     <para>
///         Only <c>Death_Clone</c> declared <c>variantMode: merge</c>, while the engine appends for
///         100 tag/owner pairs. Every other one resolved as a replacement, so the effective object
///         we compute differed from the game's for 99 tags.
///     </para>
///     <para>
///         Measured in <c>DatabaseMapClass::Map_Data_Of_Type</c> (<c>00cb5e90</c>). A variant is
///         built by copying the whole base and re-parsing the variant's own XML on top with an
///         overlay flag; 17 type codes append with no reset on either path, so the variant's entries
///         are ADDED to the base's. Which code a tag carries is per OWNING TYPE, so the list comes
///         from DatabaseMapExport rather than from tag names.
///     </para>
/// </remarks>
public sealed class EawSchemaVariantMergeModeTest
{
    // A spread across the owning types and the additive type codes. Each of these lands in a plain
    // DynamicVectorClass or std::vector, which nothing clears on the overlay path.
    [Theory]
    [InlineData("GameObjectType.yaml", "Starting_Spawned_Units_Tech_0")] // 0x46 spawn table
    [InlineData("GameObjectType.yaml", "Land_Terrain_Model_Mapping")]    // 0x34
    [InlineData("GameObjectType.yaml", "SFXEvent_Attack_Override")]      // 0x22
    [InlineData("GameObjectType.yaml", "Faction_Anim_Subindex")]         // 0x3c
    [InlineData("Faction.yaml", "Music_Event_Tactical_Win_Vs_Faction")]  // 0x29
    [InlineData("GameConstants.yaml", "ShipNameTextFiles")]              // 0x36
    public void AdditiveTags_DeclareMergeMode(string file, string tag)
    {
        Assert.Equal(VariantMode.Merge, Tag(file, tag).VariantMode);
    }

    /// <summary>
    ///     The three codes that look additive and are not, because their container clears itself on
    ///     the next write after being assigned.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>0x1e</c>, <c>0x35</c> and <c>0x3b</c> store into a
    ///         <c>MultiNameReferenceClass</c>, whose <c>operator=</c> (<c>00cab970</c>) sets a
    ///         <c>Replace</c> flag, and whose <c>Add_Name</c> (<c>00cab9a0</c>) clears the list when
    ///         that flag is set and then clears the flag.
    ///     </para>
    ///     <para>
    ///         So copying the base over the variant arms the flag, and the variant's first value
    ///         wipes the base's entries - the same replace-on-overlay behaviour the other list types
    ///         get from an explicit `if (overlay) Clear(...)`, only deferred to the next write. These
    ///         were marked <c>merge</c> in the first pass of this work and it was wrong; 44 tags had
    ///         to be reverted.
    ///     </para>
    /// </remarks>
    [Theory]
    [InlineData("GameObjectType.yaml", "Death_Explosions")]            // 0x1e
    [InlineData("GameObjectType.yaml", "Projectile_Types")]            // 0x1e
    [InlineData("GameObjectType.yaml", "Squadron_Units")]              // 0x1e
    [InlineData("GameObjectType.yaml", "Presence_Induced_Animations")] // 0x35
    [InlineData("GameObjectType.yaml", "Death_Clone")]                 // 0x3b
    [InlineData("HeroClashType.yaml", "Involved_Hero_Types")]          // 0x1e
    public void DeferredClearTags_AreNotAdditive(string file, string tag)
    {
        Assert.Equal(VariantMode.Replace, Tag(file, tag).VariantMode);
    }

    /// <summary>
    ///     Being a list, or even a sub-object list, does not make a tag additive.
    /// </summary>
    /// <remarks>
    ///     <c>Clash_Actions</c> is type 58 (<c>0x3a</c>), which the engine clears before re-parsing
    ///     an overlay, so a variant's actions replace the base's. It looks exactly like the additive
    ///     sub-object lists in XML, which is why this is pinned: the first draft of this test
    ///     asserted the opposite and was wrong.
    /// </remarks>
    [Theory]
    [InlineData("HeroClashType.yaml", "Clash_Actions")]
    [InlineData("GameObjectType.yaml", "Tactical_Health")]
    [InlineData("GameObjectType.yaml", "Shield_Points")]
    public void TagsTheEngineClearsFirst_KeepTheDefault(string file, string tag)
    {
        Assert.Equal(VariantMode.Replace, Tag(file, tag).VariantMode);
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
            Path.GetDirectoryName(typeof(EawSchemaVariantMergeModeTest).Assembly.Location)!);
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
