// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the unit leaves behind, and how far its turrets actually swing.
//
// The two halves of A3, and they share a lens for a reason: both are answers about the unit that
// only become interesting once you are DOING something to it. Which death clone you get depends on
// what killed it - which is the weapon built in the attacker panel - and a turret's traverse is a
// number in the XML until something sweeps through it.

import type { PreviewDeathClone, PreviewTurret } from '../../protocol/modelPreview';

/**
 * One card in the Gameplay dock's Death clone section.
 *
 * A MAPPING - this damage type leaves that object behind - and deliberately nothing more. The rows
 * used to carry a joined sentence naming the model file and whether the clone plays its idle, under
 * a paragraph explaining the section; the user's answer was that all a reader needs is which damage
 * type causes which clone. The model file and the idle flag are properties OF the named object and
 * are reached through the card's jump, not restated here.
 */
export interface DeathCloneRow {
    objectId: string;
    /** The damage type this clone answers, or the catch-all label where it names none. */
    label: string;
    /**
     * Whether the clone names a model.
     *
     * False covers two cases the file cannot tell apart - the object is not defined anywhere, a
     * typo the game says nothing about, or it is defined and declares no tactical model. Either way
     * there is no wreck, which is worth a mark on the card.
     */
    resolved: boolean;
    /** Whether the weapon currently configured would produce THIS clone. */
    selected: boolean;
}

/**
 * The damage type an ordinary death falls back to.
 *
 * `Damage_Normal` is not one type among many - it is THE death, and everything else is a particular
 * way to die. Measured over foc's 134 `Death_Clone` rows: 59 name `Damage_Normal`, and the rest are
 * all special kills (`Damage_Force_Whirlwind` 27, `Damage_Force_Lightning` 27, `Damage_Crush` 11,
 * `Damage_Fire` 4). Not one row omits the type, so the catch-all below never fires on shipped data.
 */
const ORDINARY_DEATH = 'damage_normal';

/**
 * The clone the given damage type would produce.
 *
 * Three steps, in order: the exact damage type, then the ordinary `Damage_Normal` death, then a row
 * naming no type at all. The middle one was missing, and it is the one that matters - a Star
 * Destroyer killed by a turbolaser declares no `Damage_Turbolaser` clone and left NO wreck, which
 * is what "the death clone never gets triggered" was.
 *
 * Order matters in both directions: a unit that declares both a specific clone and a normal one
 * must not show the normal one for the damage the specific one covers. And 28 of the 33 objects
 * with a specific clone declare ONLY specific ones, so an ordinary kill on one of those really does
 * leave nothing behind - that is the file's own answer, not a gap.
 */
export function cloneForDamage(
    clones: readonly PreviewDeathClone[], damageType: string,
): PreviewDeathClone | null {
    const named = (type: string): PreviewDeathClone | undefined => clones.find(
        clone => (clone.damageType ?? '').toLowerCase() === type);

    return named(damageType.toLowerCase())
        ?? named(ORDINARY_DEATH)
        ?? clones.find(clone => (clone.damageType ?? '') === '')
        ?? null;
}

/** A row per declared clone, with the one the current weapon would produce marked. */
export function deathCloneRows(
    clones: readonly PreviewDeathClone[], damageType: string,
): DeathCloneRow[] {
    const chosen = cloneForDamage(clones, damageType);

    return clones.map(clone => ({
        objectId: clone.objectId,
        // A row naming no damage type is the catch-all, and saying so beats a blank cell.
        label: (clone.damageType ?? '') === '' ? 'Any other damage' : clone.damageType!,
        resolved: (clone.modelFile ?? '') !== '',
        selected: chosen !== null && chosen.objectId === clone.objectId
            && (chosen.damageType ?? null) === (clone.damageType ?? null),
    }));
}

/** How far a turret is turned at one point in a sweep. Degrees, relative to the model. */
export interface SweepPose {
    rotate: number;
    elevate: number;
}

/**
 * Where a turret points at `phase` through a sweep, with `phase` running 0 to 1.
 *
 * The extents are HALF-ANGLES about the rest angle: a 90-degree traverse is 45 either side, and
 * treating the number as a full swing would draw a reach the unit has not got. A turret that
 * declares no extent does not move at all - 53 of foc's weapon objects declare them and the rest do
 * not, and inventing one would be a claim about the unit.
 *
 * A 360-degree traverse is the exception and is treated as a continuous turn: it has no end stops,
 * so swinging it back and forth would misrepresent what it does.
 */
export function sweepAngles(turret: PreviewTurret, phase: number): SweepPose {
    const rest = turret.restAngle ?? 0;
    const rotateExtent = turret.rotateExtentDegrees ?? 0;
    const elevateExtent = turret.elevateExtentDegrees ?? 0;

    if (rotateExtent <= 0 && elevateExtent <= 0) {
        return { rotate: rest, elevate: 0 };
    }

    const rotate = rotateExtent >= 360
        ? rest + phase * 360
        : rest + Math.sin(phase * 2 * Math.PI) * (rotateExtent / 2);

    // Elevation runs on its OWN extent, which is usually far smaller - AT_AA is 360 by 45 - so one
    // number for both would tip a turret through the hull it stands on.
    return {
        rotate,
        elevate: Math.sin(phase * 2 * Math.PI) * (elevateExtent / 2),
    };
}

/** A turret the viewport can swing, and where its bones live. */
export interface TurretSweep {
    /**
     * The hardpoint or weapon that declared it.
     *
     * Carried because the sweep is a per-hardpoint control: one button swinging every turret at
     * once cannot say what it will do on a hull whose hardpoints declare different extents.
     */
    id: string;
    partId: string;
    turretBone: string;
    barrelBone: string | null;
    turret: PreviewTurret;
}

/** Anything that can declare a turret: a hardpoint, or a weapon on the unit itself. */
export interface TurretSource {
    id: string;
    partId: string;
    turret?: PreviewTurret | null;
}

/**
 * Every turret on the subject that can actually be swung.
 *
 * BOTH sources, and that is the whole point of this function. The AT-AA's turret is declared on its
 * unit WEAPON - `B_Turret_Base` / `B_Missile_Launcher` at 360 by 45 - and it has no hardpoints at
 * all, so reading hardpoints alone found nothing to sweep on the very unit the feature exists for.
 * Measured on a live server.
 *
 * Deduplicated by part and bone, because a hardpoint weapon carries its hardpoint's turret too: the
 * naive union sweeps one bone twice and the second pass fights the first.
 */
export function turretSweeps(
    hardpoints: readonly TurretSource[], weapons: readonly TurretSource[],
): TurretSweep[] {
    const seen = new Set<string>();
    const sweeps: TurretSweep[] = [];

    for (const source of [...hardpoints, ...weapons]) {
        const turret = source.turret;
        const bone = turret?.turretBone ?? '';

        // A turret with no bone has nothing to turn, and one with no traverse has nowhere to turn
        // to - inventing either would claim a reach the unit has not got.
        if (turret === null || turret === undefined || bone === ''
            || ((turret.rotateExtentDegrees ?? 0) <= 0
                && (turret.elevateExtentDegrees ?? 0) <= 0)) {
            continue;
        }

        // `Set.add` returns the SET, not whether it was new - a truthy value every time, so the
        // guard has to ask `has` first.
        const key = `${source.partId}:${bone.toLowerCase()}`;
        if (seen.has(key)) {
            continue;
        }

        seen.add(key);

        sweeps.push({
            id: source.id,
            partId: source.partId,
            turretBone: bone,
            barrelBone: turret.barrelBone ?? null,
            turret,
        });
    }

    return sweeps;
}
