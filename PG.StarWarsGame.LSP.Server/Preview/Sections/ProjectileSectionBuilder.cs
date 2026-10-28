// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Server.Preview.Sections;

/// <summary>
///     What a scene knows about projectiles: the ones its weapons fire, resolved with their flight
///     and damage fields, and the catalogue of every projectile the tree defines, by name (#314).
/// </summary>
public sealed class ProjectileSectionBuilder(IVariantTagSource tagSource, IMessageCatalog messages)
{
    /// <summary>
    ///     Every projectile the tree defines, resolved.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The attacker panel picks from all of them, not from the handful this subject happens
    ///         to fire - you are building a weapon to shoot AT the subject, so its own armament is
    ///         the wrong list entirely.
    ///     </para>
    ///     <para>
    ///         NAMES only. Resolving all 105 through the variant chain was measured at 830ms added
    ///         to every scene open - a real cost for a list most readers scroll past - against about
    ///         nothing for the behaviour scan that finds them. The values for the ones this subject
    ///         actually fires are already on the wire; the rest are fetched when one is picked.
    ///     </para>
    /// </remarks>
    /// <summary>
    ///     The key the catalogue is derived under, kept on the index snapshot (#311), because
    ///     finding it costs a parse of the whole workspace.
    /// </summary>
    /// <remarks>
    ///     <c>TryGetTags</c> parses a document the first time anything asks about an object in it,
    ///     so reading a behaviour off EVERY object pulls every XML file through the parser -
    ///     measured at about 830ms on the eaw tree. Derived from the index instance, the cost is
    ///     paid once rather than on every scene open. It should really move off the scene request
    ///     altogether and onto one the panel makes when the attacker section first opens.
    /// </remarks>
    private const string ProjectileCatalogKey = "projectileCatalog";

    public List<string> ProjectileCatalog(GameIndex index)
    {
        return index.Derived(ProjectileCatalogKey, BuildProjectileCatalog);
    }

    private List<string> BuildProjectileCatalog(GameIndex index)
    {
        // By BEHAVIOUR, which the user pointed at and which measurement then settled outright: over
        // the eaw tree, `PROJECTILE` in a behaviour list appears on 0 non-projectiles, and following
        // `Variant_Of_Existing_Type` from the objects that declare it reaches 102 of 102. Exact,
        // both ways.
        //
        // Nothing else works. Every game object shares the one registered type - `GameObjectType`
        // covers units, structures, props, planets and projectiles alike - and the best tag marker,
        // `Projectile_Damage`, is on only 83% of projectiles and on 58 hardpoints besides.
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, _) in index.WorkspaceDefinitions)
            if (DeclaresProjectileBehavior(index, id))
                declared.Add(id);

        // Then everything that VARIES one of them. Read off the symbol's own `VariantBaseId`, so no
        // object has to be resolved to find out - 54 of foc's 173 projectiles are variants that
        // restate nothing but their model and colour.
        var ids = new SortedSet<string>(declared, StringComparer.OrdinalIgnoreCase);

        foreach (var id in index.WorkspaceDefinitions.Keys)
        {
            // A projectile is a game object, and so is the base it varies.
            var current = index.ResolveOfType(id, GameTypeNames.GameObjectType);

            // Bounded by the object count: a mod can write a variant cycle, and the preview reports
            // one rather than being attached to it.
            for (var hops = 0; current is { VariantBaseId: { Length: > 0 } baseId } && hops < 64; hops++)
            {
                if (declared.Contains(baseId))
                {
                    ids.Add(id);
                    break;
                }

                current = index.VariantBaseOf(current);
            }
        }

        return [.. ids];
    }

    /// <summary>Whether an object's own behaviour lists name <c>PROJECTILE</c>.</summary>
    /// <remarks>
    ///     Read from the raw tag sources rather than through the resolver: this runs over every
    ///     object in the workspace, and a variant chain per object would turn a dictionary scan into
    ///     real work. Inheritance is handled separately, by walking the symbols' own base ids.
    /// </remarks>
    private bool DeclaresProjectileBehavior(GameIndex index, string objectId)
    {
        return PreviewTagReader.BehaviorListTags
            .SelectMany(tagName =>
                RepeatedTagReader.Values(index, tagSource, objectId, tagName, GameTypeNames.GameObjectType))
            .SelectMany(value => value.Split(
                ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Any(token => token.Equals("PROJECTILE", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     The projectiles this subject's weapons name, one entry per DISTINCT projectile.
    /// </summary>
    /// <remarks>
    ///     Only what this subject fires, not the 173 in the tree. Deduplicated because a ship's
    ///     mirrored batteries fire the same bolt and a copy each would have the client build the
    ///     same material twice.
    /// </remarks>
    public List<PreviewProjectile> Projectiles(
        EffectiveObjectResolver resolver,
        IReadOnlyList<PreviewWeapon> weapons,
        List<PreviewProblem> problems)
    {
        return ProjectilesFor(
            resolver,
            weapons.Select(w => w.ProjectileType).Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!),
            problems,
            messages);
    }

    /// <summary>Resolves a list of projectile ids, skipping repeats.</summary>
    /// <remarks>
    ///     Internal rather than private because <c>aet/getProjectile</c> resolves one on demand for
    ///     the attacker panel, which offers the whole tree and not just what this subject fires. The
    ///     twenty-odd tags below are the definition of what a projectile IS to the preview, and a
    ///     second reader of them would be free to drift.
    /// </remarks>
    public static List<PreviewProjectile> ProjectilesFor(
        EffectiveObjectResolver resolver,
        IEnumerable<string> ids,
        List<PreviewProblem> problems,
        IMessageCatalog messages)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectiles = new List<PreviewProjectile>();

        foreach (var id in ids.Where(seen.Add))
        {
            var effective = resolver.Resolve(id, GameTypeNames.GameObjectType);
            if (!effective.Found)
            {
                problems.Add(new PreviewProblem(DiagnosticIds.PreviewProjectileNotDefined, "warning",
                    messages.Format("PreviewScene_ProjectileNotDefinedThisProject", id)));
                continue;
            }

            var model = effective.ValueOf("Space_Model_Name") ?? effective.ValueOf("Land_Model_Name");

            projectiles.Add(new PreviewProjectile(
                id,
                effective.BoolOf("Projectile_Custom_Render")
                    ? PreviewProjectileRender.CustomQuad
                    : PreviewProjectileRender.Model,
                model,
                effective.FloatOf("Projectile_Width"),
                effective.FloatOf("Projectile_Length"),
                effective.ValueOf("Projectile_Texture_Slot"),
                effective.ValueOf("Projectile_Laser_Color"),
                effective.FloatOf("Max_Speed"),
                effective.FloatOf("Projectile_Max_Flight_Distance"),
                effective.FloatOf("Max_Rate_Of_Turn"),
                effective.FloatOf("Projectile_Damage"),
                effective.ValueOf("Damage_Type"),
                effective.ValueOf("Projectile_Category"),
                effective.BoolOf("Projectile_Does_Shield_Damage"),
                effective.BoolOf("Projectile_Does_Energy_Damage"),
                effective.BoolOf("Projectile_Does_Hitpoint_Damage"),
                effective.FloatOf("Projectile_Blast_Area_Damage"),
                effective.FloatOf("Projectile_Blast_Area_Range"),
                effective.IntOf("Projectile_Blast_Area_Max_Victims"),
                effective.BoolOf("Projectile_Blast_Area_Dropoff"),
                effective.IntOf("Projectile_Blast_Area_Dropoff_Tiers"),
                effective.ValueOf("Projectile_Object_Detonation_Particle"),
                effective.ValueOf("Projectile_Absorbed_By_Shields_Particle"),
                effective.ValueOf("Projectile_SFXEvent_Detonate")));
        }

        return projectiles;
    }
}