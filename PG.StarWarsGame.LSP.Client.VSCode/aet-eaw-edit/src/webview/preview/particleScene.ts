// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Bulk switches over a scene's particle systems.
//
// There are three levels of "is this effect drawn?", and each has exactly one home. The MASTER - draw
// effects at all - is a scene control and lives on the viewport beside the grid. A SYSTEM is one
// proxy and lives as its own row in the model tree, under the bone it hangs off. This module is the
// level between: a GROUP, which is a named set of systems switched together.
//
// A group holds no state of its own. It names systems and reads their visibility back to work out
// whether it is on, off or split - so the tree stays the single source of truth and the panel can
// never disagree with it. An earlier version kept a `hiddenGroups` set beside the tree's own state,
// and the two drifted apart exactly as often as you would expect.
//
// Groups OVERLAP by design: one damage effect is both "hardpoint damage" and
// "p_hp_imperial_damage", and neither reading is wrong. That is why this is a flat list of bulk
// switches rather than a second tree - a tree would force each system into one parent and make the
// reader pick which truth to file it under.

import {
    PREVIEW_PARTICLE_GATE,
    type PreviewHardpoint,
    type PreviewParticle,
} from '../../protocol/modelPreview';

/** Which grouper produced a group, so the panel can section and order them. */
export type ParticleGroupSource = 'role' | 'name';

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
 * Everything a grouper is allowed to look at.
 *
 * A record rather than a bare particle array on purpose. The semantic groups the reader actually
 * wants - explosions, ability effects, one weapon's whole signature - are not derivable from the
 * particle list alone; they need the object's own data, which the Gameplay work will put on the
 * scene. Passing a context means those groupers can be added without touching the ones already
 * here or the panel that draws them.
 */
export interface GroupingContext {
    particles: readonly PreviewParticle[];
    hardpoints: readonly PreviewHardpoint[];
}

/** Turns a scene into groups. Pure, so every grouping rule is testable on its own. */
export type ParticleGrouper = (context: GroupingContext) => ParticleGroup[];

/**
 * What each gate means to a reader, in the order the panel lists them.
 *
 * Role is the one grouping the current scene data can answer honestly. `gate` is assigned by the
 * server from `Engine_Particles` and `Damage_Particles`, so these are the model's own statements
 * about what its effects are FOR rather than a guess made from a name.
 */
const ROLES: { gate: string; label: string }[] = [
    { gate: PREVIEW_PARTICLE_GATE.hardpointAlive, label: 'Engines' },
    { gate: PREVIEW_PARTICLE_GATE.hardpointDestroyed, label: 'Hardpoint damage' },
    { gate: PREVIEW_PARTICLE_GATE.always, label: 'Ambient' },
];

/** Groups by what the effect is for: engine glow, damage smoke, or neither. */
const roleGrouper: ParticleGrouper = ({ particles }) => ROLES.map(({ gate, label }) => ({
    id: `role:${gate}`,
    label,
    source: 'role' as const,
    ids: particles.filter(particle => particle.gate === gate).map(particle => particle.id),
}));

/**
 * Groups by system name.
 *
 * Arbitrary in the sense that a name is not a statement of purpose - but a Star Destroyer carries
 * twenty `p_hp_imperial_damage` proxies and switching all twenty at once is exactly what someone
 * reaching for this panel wants. Kept until the semantic groupers can do better.
 */
const nameGrouper: ParticleGrouper = ({ particles }) => {
    const byName = new Map<string, string[]>();

    for (const particle of particles) {
        const ids = byName.get(particle.systemRef);
        if (ids === undefined) {
            byName.set(particle.systemRef, [particle.id]);
        } else {
            ids.push(particle.id);
        }
    }

    return [...byName.entries()]
        // Plain comparison, not localeCompare, which orders differently per machine locale.
        .sort((a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0))
        .map(([systemRef, ids]) => ({
            id: `name:${systemRef}`,
            label: systemRef,
            source: 'name' as const,
            ids,
        }));
};

/** The groupers that run, in the order their groups are listed. */
export const PARTICLE_GROUPERS: ParticleGrouper[] = [roleGrouper, nameGrouper];

/**
 * Every bulk switch this scene offers.
 *
 * A group of one is dropped: it says exactly what that system's own tree row already says, and a
 * panel full of those is the second list the one tree was meant to replace.
 */
export function particleGroups(
    context: GroupingContext, groupers: readonly ParticleGrouper[] = PARTICLE_GROUPERS,
): ParticleGroup[] {
    return groupers
        .flatMap(grouper => grouper(context))
        .filter(group => group.ids.length > 1);
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
