// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     What a unit turns into when it dies, and what killed it decides which one.
/// </summary>
/// <remarks>
///     <para>
///         <c>Death_Clone</c> is <c>&lt;Death_Clone&gt; Damage_Type, Object_Id &lt;/...&gt;</c> - 345
///         rows in foc and 134 in eaw, and 344 of the 345 carry exactly those two fields. The damage
///         type is the interesting half: it is what ties this to the attacker panel, since the
///         weapon a reader builds decides which clone the target would leave behind.
///     </para>
///     <para>
///         It is the ONE tag in the whole schema with <c>variantMode: merge</c> plus
///         <c>multipleAllowed</c>, so the resolver keeps every occurrence and this needs no
///         <c>RepeatedTagReader</c> - unlike the reticle and armor tables, which it collapses.
///         Merge is also additive across a variant chain, which the schema states outright: a
///         re-skinned variant keeps the base's clones as well as its own.
///     </para>
/// </remarks>
public sealed class PreviewDeathCloneTest
{
    [Fact]
    public void DeathClones_ReadEveryRow_NotJustTheLastOne()
    {
        // A unit commonly names several - one per damage type that can kill it differently.
        var scene = Scene(
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Death_Clone", " Damage_Normal, Star_Destroyer_Death_Clone "),
            Tag("Death_Clone", " Damage_Force_Lightning, Bothan_Lightning_Death_Clone "));

        Assert.Equal(
            ["Star_Destroyer_Death_Clone", "Bothan_Lightning_Death_Clone"],
            scene.DeathClones.Select(c => c.ObjectId));
        Assert.Equal(
            ["Damage_Normal", "Damage_Force_Lightning"],
            scene.DeathClones.Select(c => c.DamageType));
    }

    [Fact]
    public void DeathClones_CarryTheCloneModel_SoThePreviewCanShowIt()
    {
        var scene = Scene(
            [Sym("Ship", "SpaceUnit"), Sym("Wreck", "SpaceUnit")],
            tags => tags.With("Wreck", Tag("Space_Model_Name", "EV_StarDestroyer_D.ALO")),
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Death_Clone", "Damage_Normal, Wreck"));

        Assert.Equal("EV_StarDestroyer_D.ALO", Assert.Single(scene.DeathClones).ModelFile);
    }

    [Fact]
    public void DeathClones_ReportACloneThatIsNotDefined()
    {
        // A typo here costs the wreck entirely, and the game says nothing. Reported at warning
        // because it IS a mistake, unlike an unbound ability effect.
        var scene = Scene(
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Death_Clone", "Damage_Normal, No_Such_Clone"));

        Assert.Contains(scene.Problems,
            p => p.Severity == "warning" && p.Message.Contains("No_Such_Clone"));
    }

    [Fact]
    public void DeathClones_DropAMalformedRow()
    {
        // One of foc's 345 rows is empty. A row with no object names no clone, and padding it would
        // invent one.
        var scene = Scene(
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Death_Clone", ""),
            Tag("Death_Clone", "Damage_Normal"),
            Tag("Death_Clone", "Damage_Normal, Wreck"));

        Assert.Equal(["Wreck"], scene.DeathClones.Select(c => c.ObjectId));
    }

    [Fact]
    public void DeathClones_CarryTheObjectsPlayIdleFlag()
    {
        // `Should_Death_Clone_Play_Idle` is a Boolean on the OBJECT rather than a third field on the
        // row, so it is the same answer for every clone the object names. 28 shipped uses, all
        // `true`.
        var scene = Scene(
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Should_Death_Clone_Play_Idle", "true"),
            Tag("Death_Clone", "Damage_Normal, Wreck"));

        Assert.True(Assert.Single(scene.DeathClones).PlaysIdle);
    }

    [Fact]
    public void DeathClones_DefaultToNotPlayingAnIdle()
    {
        var scene = Scene(
            Tag("Space_Model_Name", "hull.alo"), Tag("Death_Clone", "Damage_Normal, Wreck"));

        Assert.False(Assert.Single(scene.DeathClones).PlaysIdle);
    }

    [Fact]
    public void DeathClones_AreEmptyForAnObjectThatNamesNone()
    {
        Assert.Empty(Scene(Tag("Space_Model_Name", "hull.alo")).DeathClones);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static PreviewScene Scene(params VariantTag[] shipTags)
    {
        return Scene([Sym("Ship", "SpaceUnit")], tags => tags, shipTags);
    }

    private static PreviewScene Scene(
        GameSymbol[] symbols,
        Func<FakeVariantTagSource, FakeVariantTagSource> extra,
        params VariantTag[] shipTags)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };

        var tags = extra(new FakeVariantTagSource().With("Ship", shipTags));

        return new PreviewSceneBuilder(new FakeGameIndexService(index), new DeathCloneSchema(),
            tags, new FakeAssets("hull.alo")).BuildForObject("Ship");
    }

    private static GameSymbol Sym(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin($"file:///{id}.xml", 0, 0), null, null);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    /// <summary>
    ///     A schema that says what the real one says about <c>Death_Clone</c>.
    /// </summary>
    /// <remarks>
    ///     Not the bare <c>NullSchemaProvider</c>, deliberately. Reporting no schema makes the
    ///     resolver treat every tag as <c>Replace</c>, which collapses the occurrence list to the
    ///     last row - so a test on the null provider would prove the OPPOSITE of production, where
    ///     <c>GameObjectType.yaml</c> declares this tag <c>variantMode: merge</c> plus
    ///     <c>multipleAllowed</c>. That mode is also what makes a variant keep its base's clones.
    /// </remarks>
    private sealed class DeathCloneSchema : NullSchemaProvider
    {
        public override XmlTagDefinition? GetTag(string tagName)
        {
            return tagName.Equals("Death_Clone", StringComparison.OrdinalIgnoreCase)
                ? new XmlTagDefinition
                {
                    Tag = "Death_Clone",
                    ValueType = XmlValueType.DeathCloneSpec,
                    MultipleAllowed = true,
                    VariantMode = VariantMode.Merge
                }
                : null;
        }
    }

    [Fact]
    public void BuildForObject_CarriesTheSubjectsOwnDeathExplosion()
    {
        // `Death_Explosions` on the UNIT is a third, separate thing from the hardpoint's and the
        // breakoff prop's. It never reached the wire, so nothing went off when the ship died.
        var scene = Scene(
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Death_Explosions", "Large_Explosion_Space"));

        Assert.Equal("Large_Explosion_Space", scene.DeathExplosions);
    }

    [Fact]
    public void BuildForObject_LeavesTheDeathExplosionNullWhenNoneIsDeclared()
    {
        Assert.Null(Scene(Tag("Space_Model_Name", "hull.alo")).DeathExplosions);
    }

    /// <summary>
    ///     Spinning away: the automated death clone for a unit that declares none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The user's own account of the mechanic: a boolean tag, and where the unit has no death
    ///         clone it keeps going along its current vector at its current speed, corkscrewing, and
    ///         explodes at the end.
    ///     </para>
    ///     <para>
    ///         Measured over both trees: <b>34 objects declare it and every one says Yes. NOT ONE of
    ///         the 34 also declares a Death_Clone</b>, which is the rule holding in the data. All 34
    ///         declare <c>Max_Speed</c>. The engine's own parameter table
    ///         (<c>DatabaseMapExport.xml</c>) names five tags in the family, not one - the chance,
    ///         the time, its own explosion and a start sound - so none of this is guessed.
    ///     </para>
    ///     <para>
    ///         Read off the TAG rather than the locomotor. The 34 are not only fighters - they
    ///         include a <c>GroundVehicle</c> and a <c>LandBombingUnit</c> - and no shipped object
    ///         declares the tag without meaning it.
    ///     </para>
    /// </remarks>
    [Fact]
    public void SpinAway_IsReadWithItsTimeChanceAndExplosion()
    {
        var scene = Scene(
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Spin_Away_On_Death", "Yes"),
            Tag("Spin_Away_On_Death_Time", "2.0f"),
            Tag("Spin_Away_On_Death_Chance", "0.2"),
            Tag("Spin_Away_On_Death_Explosion", "Small_Explosion_Space"),
            Tag("Max_Speed", "4.5"));

        var spin = scene.SpinAway;

        Assert.NotNull(spin);
        Assert.Equal(2.0f, spin!.TimeSeconds);
        Assert.Equal(0.2f, spin.Chance);
        Assert.Equal("Small_Explosion_Space", spin.Explosion);
        Assert.Equal(4.5f, spin.MaxSpeed);
    }

    /// <summary>
    ///     The <c>f</c> suffix is in the shipped files - 31 write <c>2.0f</c> and 3 write
    ///     <c>1.0f</c>. Reading it as a plain float drops every one of them to the default.
    /// </summary>
    [Fact]
    public void SpinAway_ReadsTheFloatSuffixTheFilesActuallyWrite()
    {
        var scene = Scene(
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Spin_Away_On_Death", "Yes"),
            Tag("Spin_Away_On_Death_Time", " 1.0f "));

        Assert.Equal(1.0f, scene.SpinAway!.TimeSeconds);
    }

    [Fact]
    public void SpinAway_IsAbsentWhereTheTagIsOffOrMissing()
    {
        Assert.Null(Scene(Tag("Space_Model_Name", "hull.alo")).SpinAway);
        Assert.Null(Scene(
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Spin_Away_On_Death", "No")).SpinAway);
    }
}
