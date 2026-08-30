// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Bulk switches over a scene's particle systems.
//
// There are three levels of "is this effect drawn?", and each has exactly one home. The MASTER - draw
// effects at all - is a scene control and lives on the viewport beside the grid. A SYSTEM is one
// proxy and lives as its own row in the model tree, under the bone it is attached to. This module is the
// level between: a GROUP, which is a named set of systems switched together.
//
// A group holds no state of its own. It names systems and reads their visibility back to work out
// whether it is on, off or split - so the tree stays the single source of truth and the panel can
// never disagree with it. An earlier version kept a `hiddenGroups` set beside the tree's own state,
// and the two drifted apart exactly as often as you would expect.
//
// A group is a MEANING - the engines, the hardpoint damage, the power-to-weapons effects - and each
// meaning appears exactly once. Two rules find them, because neither alone can: the GATE the model
// assigns, and the marker PREFIX the artist writes into the name. See `FAMILIES`.
//
// It stays a flat list of bulk switches rather than a second tree. A tree would force each system
// into one parent, and a system genuinely can belong to two meanings at once.

import {
    PREVIEW_PARTICLE_GATE,
    type PreviewHardpoint,
    type PreviewParticle,
} from '../../protocol/modelPreview';

/**
 * Where a loaded particle system came from.
 *
 * `model` is a proxy the subject itself declares - it is in the scene's particle list, it names a
 * bone, and it is part of what the asset IS. `gameplay` is everything the Gameplay lens asks for by
 * NAME at the moment it needs it: a hardpoint's death explosion, a wreck's trailing fire, an
 * object-level effect. Those are in no proxy list and hang off no bone.
 *
 * Stated by the caller rather than inferred. The two arrive through the same `addParticleSystem`,
 * and every attempt to tell them apart afterwards has to guess from the anchor - which is exactly
 * what put `p_explosion_big00` under Root in the model tree with a checkbox that did nothing.
 */
export type ParticleOrigin = 'model' | 'gameplay';

/**
 * Whether a loaded effect belongs in the MODEL tree.
 *
 * The tree is a list of what the asset is made of. A gameplay effect is not part of it: it has no
 * place in the hierarchy, so `ownerOf` files it under the root bone by fallback, and its visibility
 * is driven by the event that fired it rather than by the row chain - which is why its tick looked
 * dead. It stays visible to the Gameplay lens, which is what owns it.
 */
export function listedInModelTree(origin: ParticleOrigin): boolean {
    return origin === 'model';
}

/** Whether a group is a named family or a stem nothing could be said about. */
export type ParticleGroupSource = 'family' | 'prefix';

/** One bulk-switchable set of particle systems. */
export interface ParticleGroup {
    /** Stable across loads, so React keys and the panel's ordering survive a reload. */
    id: string;
    label: string;
    source: ParticleGroupSource;
    /** The scene ids of the systems in it. Ids, not indices - a system's id survives a reload. */
    ids: string[];
}

/**
 * Everything the grouping is allowed to look at.
 *
 * A record rather than a bare particle array on purpose. A future family may need the object's own
 * data - one weapon's whole signature, say - and passing a context means it can be added without
 * touching the ones already here or the panel that draws them.
 */
export interface GroupingContext {
    particles: readonly PreviewParticle[];
    hardpoints: readonly PreviewHardpoint[];
}

/**
 * The marker token an effect name starts with.
 *
 * Measured over both shipped trees, 1957 assets: the leading token is the artist's statement of
 * what KIND of effect this is - 66 `pe_` engine glows, 6 `pte_` turbo engines, 5 `pptw_`, 3 each of
 * `prs_`, `pgw_` and `pi_`.
 *
 * The bare `p` is the exception, and it is not a small one: 502 of the 1957 start with it, so on its
 * own it says nothing beyond "this is a particle" and would put every generic effect on the model
 * under one heading. There the token AFTER it is the name - `p_hp` is the hardpoint damage family,
 * `p_explosion` the blasts.
 *
 * The whole TOKEN, never the leading characters: `pte_` starts with `pt`, not with `pe`, and the
 * Corvette and the Tartan both carry `pe_` and `pte_` effects that must not merge.
 */
export function effectPrefix(systemRef: string): string {
    const parts = systemRef.toLowerCase().split('_');

    return parts[0] === 'p' && parts.length > 1 ? `p_${parts[1]}` : parts[0];
}

/**
 * A named kind of effect, and the two ways one can be recognised.
 *
 * `gates` is what the MODEL says - the server assigns them from `Engine_Particles` and
 * `Damage_Particles`, so a gate is the object's own statement about what an effect is for.
 * `prefixes` is what the ARTIST says, in the name. Both are evidence for the same question and a
 * family takes the union of what they find.
 *
 * One family per meaning is the whole point of the rewrite. There used to be two groupers - one
 * over gates, one over whole system names - listing the same systems twice: a Nebulon-B showed
 * `Hardpoint damage` and `p_hp_stardestroyer_damage` as separate rows over the same six proxies, so
 * ticking either moved the other and nothing on screen said why.
 *
 * Neither rule alone is enough, which is why both are here. The Tartan and the Corvette carry their
 * engine effects on gate `Always`, because neither declares an engine hardpoint - a gate-only rule
 * finds no engines at all on them, which is the missing engine switch that was reported. And a
 * name-only rule splits Home One's `pe_homeoneengines_lrg/_med/_sml` into three groups of one.
 *
 * Only meanings that can be defended are named. `pas_` (`Pas_sprint`) and `pem_`
 * (`Pem_invulnerability`) are ability markers whose expansion is a guess, so they are left to the
 * prefix fallback rather than given a label this file cannot stand behind.
 */
interface EffectFamily {
    id: string;
    label: string;
    /** Gates the model assigns, if any. */
    gates?: readonly string[];
    /** Marker prefixes, as {@link effectPrefix} reads them. */
    prefixes?: readonly string[];
    /**
     * The family this one REPLACES while an ability is driving it. See {@link replacedByAbility}.
     *
     * One pair, and it is the user's rule rather than an inference: turbo shows the power-to-engine
     * effect and hides the normal engine effect. Whether power to weapons does the same to a
     * weapon effect is not known, so nothing here says it does.
     */
    replaces?: string;
}

/** The families, in the order the panel lists them. Fixed, so the list never shuffles. */
const FAMILIES: readonly EffectFamily[] = [
    {
        id: 'engines',
        label: 'Engines',
        gates: [PREVIEW_PARTICLE_GATE.hardpointAlive],
        prefixes: ['pe'],
    },
    {
        id: 'hardpointDamage',
        label: 'Hardpoint damage',
        gates: [PREVIEW_PARTICLE_GATE.hardpointDestroyed],
        prefixes: ['p_hp'],
    },
    { id: 'turbo', label: 'Turbo engines', prefixes: ['pte'], replaces: 'engines' },
    { id: 'powerToWeapons', label: 'Power to weapons', prefixes: ['pptw'] },
    { id: 'missileShield', label: 'Missile shield', prefixes: ['prs'] },
    { id: 'gravityWell', label: 'Gravity well', prefixes: ['pgw'] },
    // ION STUN, not "internal damage" - the user's correction, and the name was a guess from the
    // start. An ion-stunned ship is what this draws: the tag is `Projectile_Ion_Stun_On_Detonation`,
    // carried by exactly TWO shipped projectiles (`Proj_Ion_Cannon_Planetary` and
    // `Proj_Ion_Cannon_Medium_Laser_Blue`), each alongside a stun duration, a speed reduction, a
    // shot-rate reduction and `Projectile_Disable_Engines_Duration`.
    //
    // The three assets agree: `Pi_damage_elec_cap00`, `_sd00` and `_small00` - electrical damage in
    // capital, Star Destroyer and small sizes, which is what a stunned hull crackles with.
    { id: 'ionStun', label: 'Ion stun', prefixes: ['pi'] },
];

/** Whether a family recognises this system, by either kind of evidence. */
function claims(family: EffectFamily, particle: PreviewParticle): boolean {
    return (family.gates?.includes(particle.gate) ?? false)
        || (family.prefixes?.includes(effectPrefix(particle.systemRef)) ?? false);
}

/**
 * The effects held back because an active ability replaces their family.
 *
 * The reported fault was TURBO on the CR90 "not toggleable". Measured on the running panel, the key
 * was enabled and the turbo plume did light - what never happened is the plain engine plume going
 * out, so both burned from the same nozzles and pressing the key looked like it did nothing.
 *
 * Nothing else in the chain can express this. `abilityAllows` gates the proxies an ability CLAIMS,
 * and deliberately leaves every other effect alone - otherwise activating one ability would silence
 * the whole model. Suppressing an effect an ability does not claim is a different statement, and
 * this is the only place that makes it.
 *
 * Driven by the ABILITY, not by the effect being visible: ticking the turbo row in Model mode is a
 * reader inspecting the asset, and killing another row's effect underneath them would fight the
 * rule that the reader's word wins. In Model mode nothing is active, so nothing is held back.
 */
export function replacedByAbility(
    particles: readonly PreviewParticle[],
    proxies: ReadonlyMap<string, string[]>,
    active: ReadonlySet<string>,
): Set<string> {
    const driven = new Set<string>();

    for (const [type, ids] of proxies) {
        if (active.has(type)) {
            for (const id of ids) {
                driven.add(id);
            }
        }
    }

    const held = new Set<string>();
    if (driven.size === 0) {
        return held;
    }

    for (const family of FAMILIES) {
        if (family.replaces === undefined) {
            continue;
        }

        // The ability has to be driving THIS family. An active ability that claims something else
        // entirely leaves the engines alone - the Tartan carries pe_, pte_ and pptw_ at once.
        const driving = particles.some(
            particle => driven.has(particle.id) && claims(family, particle));

        if (!driving) {
            continue;
        }

        const replaced = FAMILIES.find(other => other.id === family.replaces);
        if (replaced === undefined) {
            continue;
        }

        for (const particle of particles) {
            // Never the effect doing the replacing, which a family could otherwise claim twice -
            // `pte_` is matched by prefix and an engine gate would match it again.
            if (claims(replaced, particle) && !driven.has(particle.id)) {
                held.add(particle.id);
            }
        }
    }

    return held;
}

/**
 * Every bulk switch this scene offers.
 *
 * A named family is kept at ONE member. That is deliberate and it is the reported fault: the
 * Nebulon-B carries a single `pe_nebulonengines`, the old floor of two dropped it, and the one
 * group a reader wanted on that ship was the one it never showed. A family of one is still a
 * meaning - it says "these are the engines" where the row only says a name.
 *
 * A group that could only ever repeat its own prefix keeps the floor. `Pas_sprint` alone is one
 * system and one row, and a heading over it with nothing to add is the second list the one tree was
 * meant to replace.
 *
 * There is no catch-all for what is left. `Ambient` used to be every ungated system on the model
 * under one heading, which on the Tartan was all seven of them - engines, turbo engines and ability
 * effects together. A group meaning "the rest" is not a bulk switch anyone reaches for.
 */
export function particleGroups(context: GroupingContext): ParticleGroup[] {
    const { particles } = context;
    const groups: ParticleGroup[] = [];
    const named = new Set<string>();

    for (const family of FAMILIES) {
        const ids = particles.filter(particle => claims(family, particle))
            .map(particle => particle.id);

        if (ids.length === 0) {
            continue;
        }

        for (const id of ids) {
            named.add(id);
        }

        groups.push({ id: `family:${family.id}`, label: family.label, source: 'family', ids });
    }

    // What no family recognised, gathered under the only thing that can honestly be said about it.
    // A mod's own effects land here, and so does any shipped family nobody has named yet.
    const byPrefix = new Map<string, string[]>();

    for (const particle of particles) {
        if (named.has(particle.id)) {
            continue;
        }

        const prefix = effectPrefix(particle.systemRef);
        const ids = byPrefix.get(prefix);

        if (ids === undefined) {
            byPrefix.set(prefix, [particle.id]);
        } else {
            ids.push(particle.id);
        }
    }

    const rest = [...byPrefix.entries()]
        .filter(([, ids]) => ids.length > 1)
        // Plain comparison, not localeCompare, which orders differently per machine locale.
        .sort((a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0))
        .map(([prefix, ids]) => ({
            id: `prefix:${prefix}`,
            label: prefixLabel(prefix),
            source: 'prefix' as const,
            ids,
        }));

    return [...groups, ...rest];
}

/** `p_krayt` as a heading. Enough to scan; not a translation, because none can be asserted. */
function prefixLabel(prefix: string): string {
    const spaced = prefix.replace(/_/g, ' ');

    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

/** Whether a group is fully on, fully off, or split. */
export type GroupState = 'all' | 'some' | 'none';

/**
 * What a group's switch should read, given which systems are actually drawn.
 *
 * Derived rather than stored - that is the whole point. Switching one system off in the tree makes
 * its groups read `some` without anything having to tell them.
 */
export function groupState(group: ParticleGroup, shown: ReadonlySet<string>): GroupState {
    const on = group.ids.filter(id => shown.has(id)).length;

    if (on === 0) {
        return 'none';
    }

    return on === group.ids.length ? 'all' : 'some';
}

/** What switches this group on, in words. Empty when nothing does. */
export function describeGate(gate: string, hardpointId: string | null): string {
    if (gate === PREVIEW_PARTICLE_GATE.hardpointDestroyed) {
        return `when ${hardpointId ?? 'its hardpoint'} is destroyed`;
    }

    if (gate === PREVIEW_PARTICLE_GATE.hardpointAlive) {
        return `until ${hardpointId ?? 'its hardpoint'} is destroyed`;
    }

    return '';
}
