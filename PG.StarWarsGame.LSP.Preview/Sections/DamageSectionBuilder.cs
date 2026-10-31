// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Preview;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Preview.Sections;

/// <summary>
///     How a scene takes damage and dies: the target defence the attacker panel shoots at, the land
///     damage stages and their thresholds, the death clones and the spin-away, and the notice for a
///     stage the hull's model tags nothing for (#314). A death clone's proxies are the parts section's.
/// </summary>
public sealed class DamageSectionBuilder(
    IVariantTagSource tagSource,
    IGameAssetResolver assets,
    IMessageCatalog messages)
{
    private readonly PartsSectionBuilder _parts = new(assets, tagSource, messages);

    /// <summary>
    ///     Whether losing every hardpoint destroys the object: on unless the object denies it, as the
    ///     engine reads <c>Should_Be_Destroyed_When_All_Hardpoints_Destroyed</c>.
    /// </summary>
    public static bool DiesWithHardpoints(EffectiveObject effective)
    {
        return EngineBoolean.IsTrueUnlessDenied(
            effective.ValueOf("Should_Be_Destroyed_When_All_Hardpoints_Destroyed"));
    }

    /// <summary>What both shipped corpora write, and what the engine falls back to.</summary>
    private const float DefaultHullVsHardpointsConstraint = 0.2f;

    /// <summary>
    ///     What a hit on this subject has to get through.
    /// </summary>
    /// <remarks>
    ///     The previewed object is the TARGET - the attacker is a weapon the reader builds - so this
    ///     is the defensive half of the damage model and the only half that comes from the file.
    ///     Only the two armor COLUMNS this target uses are sent; see <see cref="PreviewTargetDefence" />.
    /// </remarks>
    public PreviewTargetDefence Defence(GameIndex index,
        EffectiveObject effective, IReadOnlyList<PreviewHardpoint> hardpoints)
    {
        var table = PreviewDamageTable.From(index, tagSource);

        var armor = effective.ValueOf("Armor_Type");
        var shieldArmor = effective.ValueOf("Shield_Armor_Type");

        // Only the hardpoints that can actually die. An indestructible one can never be part of a
        // death, so counting its health would put a floor under the bar that nothing could remove.
        var destructible = hardpoints.Where(h => h.IsDestroyable).ToList();

        return new PreviewTargetDefence(
            IsShielded(effective),
            string.IsNullOrWhiteSpace(armor) ? null : armor.Trim(),
            string.IsNullOrWhiteSpace(shieldArmor) ? null : shieldArmor.Trim(),
            effective.FloatOf("Shield_Points"),
            effective.FloatOf("Tactical_Health"),
            effective.FloatOf("Energy_Capacity"),
            destructible.Count == 0 ? null : destructible.Sum(h => h.Health ?? 0f),
            DiesWithHardpoints(effective),
            HullVsHardpointsConstraint(index),
            table.FactorsFor(armor),
            table.FactorsFor(shieldArmor),
            table.DamageTypes);
    }

    /// <summary>
    ///     <c>Hull_Vs_Hard_Points_Health_Constraint</c>, or the shipped 0.2 when GameConstants does
    ///     not set it to a number.
    /// </summary>
    /// <remarks>
    ///     The default is 0.2 rather than 0 deliberately: 0 would tie the two pools together
    ///     exactly, which is the opposite of the constant being absent. A value the engine could not
    ///     parse is treated the same as no value at all.
    /// </remarks>
    private float HullVsHardpointsConstraint(GameIndex index)
    {
        foreach (var value in RepeatedTagReader.Values(
                     index, tagSource, EncyclopediaTags.GameConstantsId,
                     "Hull_Vs_Hard_Points_Health_Constraint"))
            if (EngineNumber.ReadFloat(value) is { } parsed)
                return parsed;

        return DefaultHullVsHardpointsConstraint;
    }

    /// <summary>Whether any behaviour list names <c>SHIELDED</c>.</summary>
    /// <remarks>
    ///     <para>
    ///         All THREE lists, unlike <see cref="HasWeaponBehavior" />, and the asymmetry is
    ///         measured rather than assumed. Over foc, <c>SHIELDED</c> appears in
    ///         <c>SpaceBehavior</c> 98 times, in plain <c>Behavior</c> 32 and in <c>LandBehavior</c>
    ///         19; <c>WEAPON</c> never appears in plain <c>Behavior</c> at all. Reading only two
    ///         lists here would call a fifth of the shielded objects unshielded.
    ///     </para>
    ///     <para>
    ///         Whole tokens, so <c>UNSHIELDED</c> does not read as <c>SHIELDED</c>.
    ///     </para>
    /// </remarks>
    private static bool IsShielded(EffectiveObject effective)
    {
        return PreviewTagReader.BehaviorListTags
            .SelectMany(tag => (effective.ValueOf(tag) ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Any(token => token.Equals("SHIELDED", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     The damage stages an object declares, ascending and unique.
    /// </summary>
    /// <remarks>
    ///     Non-numeric entries are dropped rather than reported. This column sits beside
    ///     <c>Land_Damage_SFX</c>, which uses the literal <c>null</c> as its "no sound" placeholder.
    ///     What the preview needs from THIS column is which stages exist.
    ///     <para>
    ///         The looseness is in the SFX column alone, and worth stating precisely because it used
    ///         to read as a reason to distrust the whole table: measured over foc with XML comments
    ///         stripped, 42 objects declare an SFX count that disagrees with their alternates - but
    ///         <c>Land_Damage_Thresholds</c> disagrees in ZERO of the 219. The threshold pairing is
    ///         sound, which is what lets <see cref="DamageTable" /> rely on it.
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<int> DamageStages(EffectiveObject effective)
    {
        var raw = effective.ValueOf("Land_Damage_Alternates");
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var stages = new SortedSet<int>();
        foreach (var token in raw.Split(
                     ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(token, out var stage) && stage >= 0)
                stages.Add(stage);

        return [.. stages];
    }

    /// <summary>
    ///     The damage table as ordered bands, or empty when the object declares none usable.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The pairing is POSITIONAL, so this keeps the written order and does not sort or
    ///         de-duplicate anything - a stage may legitimately appear in more than one band.
    ///     </para>
    ///     <para>
    ///         Empty rather than salvaged when the two columns disagree in length. Inventing an
    ///         alignment would put the wrong mesh on screen at the wrong health, which is worse than
    ///         leaving the reader the manual slider. Measured over foc with XML comments stripped:
    ///         219 objects declare the table and the two columns NEVER disagree, so this guard is an
    ///         invariant being held rather than a case being handled. (The "42 objects declare
    ///         counts that do not match" beside <see cref="DamageStages" /> is about
    ///         <c>Land_Damage_SFX</c>, a different column.)
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<PreviewDamageBand> DamageTable(EffectiveObject effective)
    {
        var stages = PreviewTagReader.Numbers<int>(effective.ValueOf("Land_Damage_Alternates"), int.TryParse);
        var thresholds = PreviewTagReader.Numbers<float>(effective.ValueOf("Land_Damage_Thresholds"), float.TryParse);

        if (stages.Count == 0 || stages.Count != thresholds.Count)
            return [];

        return [.. stages.Select((stage, at) => new PreviewDamageBand(thresholds[at], stage))];
    }

    /// <summary>
    ///     Reports a declared damage stage the hull's model tags nothing for.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read off <see cref="GameIndex.ModelBones" /> - bones UNION mesh names - rather than
    ///         from a fresh read of the file, so this and the XML validator's own version of the
    ///         check answer from the same source and cannot disagree about one object. The catalogue
    ///         holds no PROXY names, which measures out as no loss: of the 149 shipped models
    ///         carrying an ALT-tagged proxy, not one tags a level that no bone or mesh also tags.
    ///     </para>
    ///     <para>
    ///         The HULL only. A hardpoint mount is its own render object and the declaration belongs
    ///         to the unit; none of the six shipped objects this fires on carries a hardpoint
    ///         anyway, so widening it to the parts would buy nothing and let a turret answer for a
    ///         stage the hull is missing.
    ///     </para>
    ///     <para>
    ///         Silent when the model is not catalogued at all. That is undecidable rather than
    ///         wrong, and the warning would be about the index rather than about the file.
    ///     </para>
    /// </remarks>
    public void ReportUntaggedStages(GameIndex index,
        string objectId, string? hull, IReadOnlyList<int> stages, List<PreviewProblem> problems)
    {
        if (hull is null || stages.Count == 0)
            return;

        if (!index.ModelBones.TryGetValue(ModelBoneKey.From(hull), out var names))
            return;

        var missing = ModelLevelTag.StagesNotTagged(stages, ModelLevelTag.AltLevelsIn(names));
        if (missing.Count == 0)
            return;

        var which = missing.Count == 1
            ? messages.Format("DamageStageNotOnModel_Stage", missing[0])
            : messages.Format("DamageStageNotOnModel_Stages", string.Join(", ", missing));

        problems.Add(new PreviewProblem(DiagnosticIds.PreviewDamageStageNotInModel, "warning",
            messages.Format("PreviewScene_DamageStageUntagged", objectId, which, hull)));
    }

    /// <summary>
    ///     What this object leaves behind when it dies, one entry per declared damage type.
    /// </summary>
    /// <remarks>
    ///     Straight off <c>effective.Tags</c>, with no <c>RepeatedTagReader</c>: <c>Death_Clone</c>
    ///     is <c>multipleAllowed</c> with <c>repeatMode: add</c>, so the resolver keeps every
    ///     occurrence an object declares, each as its own entry.
    /// </remarks>
    public List<PreviewDeathClone> DeathClones(GameIndex index,
        EffectiveObjectResolver resolver, EffectiveObject effective, List<PreviewProblem> problems)
    {
        // On the OBJECT, not on the row, so it is the same answer for every clone it names.
        var playsIdle = effective.BoolOf("Should_Death_Clone_Play_Idle");
        var clones = new List<PreviewDeathClone>();

        foreach (var tag in effective.Tags.Where(t =>
                     t.TagName.Equals("Death_Clone", StringComparison.OrdinalIgnoreCase)))
        {
            var row = tag.Value.Split(',', StringSplitOptions.TrimEntries);

            // One of foc's 345 rows is empty, and a row naming no object names no clone. Padding it
            // would invent one.
            if (row.Length < 2 || row[1].Length == 0)
                continue;

            var clone = resolver.Resolve(row[1], GameTypeNames.GameObjectType);

            if (!clone.Found)
            {
                // A real mistake, unlike an unbound ability effect: the wreck simply never appears
                // and the game says nothing about it.
                problems.Add(new PreviewProblem(DiagnosticIds.PreviewDeathCloneNotDefined, "warning",
                    messages.Format("PreviewScene_DeathCloneNotDefinedThis", row[1])));
                clones.Add(new PreviewDeathClone(PreviewTagReader.Blank(row[0]), row[1], null, playsIdle));
                continue;
            }

            var model = clone.ValueOf("Space_Model_Name") ?? clone.ValueOf("Land_Model_Name");

            // Its OWN model's clips and effects. Everything else on the wire describes the subject,
            // and a clone is a replacement for it rather than a piece of it.
            clones.Add(new PreviewDeathClone(PreviewTagReader.Blank(row[0]), row[1], model, playsIdle,
                model is null ? [] : AnimationSectionBuilder.AnimationsFor(index, model),
                model is null ? [] : _parts.ProxiesOf(model, row[1], [], problems),
                // The clone's OWN scale, read off the resolved clone - variants included - through
                // the same reader the subject's scale uses.
                EngineScaleFactor.Of(clone.ValueOf("Scale_Factor"))));
        }

        return clones;
    }

    /// <summary>
    ///     How the subject comes apart when it declares no death clone, or null where it does not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read off the TAG, not the locomotor. The 34 shipped objects that declare it are not
    ///         only fighters - they include a <c>GroundVehicle</c> and a <c>LandBombingUnit</c> - and
    ///         no shipped object declares it without meaning it, so the tag is the whole condition.
    ///     </para>
    ///     <para>
    ///         The four numbers all come out of the file. Only the corkscrew itself is invented, and
    ///         that is the client's business - nothing in any file describes it.
    ///     </para>
    /// </remarks>
    public static PreviewSpinAway? SpinAway(EffectiveObject effective)
    {
        if (!effective.BoolOf("Spin_Away_On_Death"))
            return null;

        return new PreviewSpinAway(
            // 2.0 where the file is silent: 31 of the 34 write exactly that, so it is the corpus's
            // own answer rather than a number picked to fill a gap.
            effective.FloatOf("Spin_Away_On_Death_Time") ?? 2.0f,
            effective.FloatOf("Spin_Away_On_Death_Chance") ?? 0f,
            effective.ValueOf("Spin_Away_On_Death_Explosion"),
            effective.FloatOf("Max_Speed") ?? 0f);
    }
}