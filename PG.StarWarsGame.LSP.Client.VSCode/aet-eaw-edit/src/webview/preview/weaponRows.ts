// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One weapon bank, as the Gameplay dock shows it and as the viewport draws it.
//
// The scene sends measurements; a row is what a reader can act on. Keeping the two apart is what
// lets the whole thing be tested without three.js: the dock renders these rows and the viewport
// takes `visibleArcs`, and neither has to know how a nullable `Pulse_Delay` becomes a sentence.
//
// Arcs are controlled at THREE levels, the same shape the particle systems settled into: the pill
// on the stage is the master, each row is one bank, and a fire bone is the individual thing you can
// point at in the tree. The master holds no opinion about which banks are on - switch it off and
// back on and your picks are still there.

import type {
    PreviewHardpoint, PreviewTurret, PreviewWeapon,
} from '../../protocol/modelPreview';
import type { TreeItem } from './previewTree';

import { PREVIEW_WEAPON_SOURCE } from '../../protocol/modelPreview';

/** One cone the viewport draws, tagged with the bank it belongs to. */
export interface WeaponArc {
    /** The `PreviewWeapon.id` this came from, so a bank can be switched without a rebuild. */
    weaponId: string;
    partId: string;
    bone: string;
    widthDegrees: number;
    heightDegrees: number;
    range: number;
}

/** One row in the Gameplay dock's Weapons section. */
export interface WeaponRow {
    id: string;
    /**
     * What the row is CALLED - the mount's id, or the bank's name.
     *
     * Not the type. Six laser mounts on one hull all declare `HARD_POINT_WEAPON_LASER`, so naming
     * rows by type gave six identical ones; the id is what separates them, and it is what the
     * Hardpoints list beside this one already shows.
     */
    name: string;
    /** The hardpoint's `Type`, which is what picks its reticle. Kept for the detail line. */
    label: string;
    /** `Hardpoint` or `Unit`, verbatim from the wire. */
    source: string;
    hardpointId: string | null;
    /** The part its fire bones live on: the mounted model, or the hull. */
    partId: string;
    fireBones: string[];
    /** Empty when the weapon declares no reach, so there is nothing to draw. */
    arcs: WeaponArc[];
    cadence: string | null;
    damage: string | null;
    reach: string | null;
    cone: string | null;
    projectileId: string | null;
    fireModes: string[];
    /** The turret this bank sits on, when it declares one. Read by the sweep. */
    turret: PreviewTurret | null;
    /** Its mount has been shot away, so the game would no longer fire it. */
    destroyed: boolean;
}

/** What `weaponRows` needs off the scene. */
export interface WeaponScene {
    weapons: readonly PreviewWeapon[];
    hardpoints: readonly PreviewHardpoint[];
}

/**
 * Builds a row per weapon bank.
 *
 * `destroyed` is the set of hardpoint ids the reader has blown off, exactly as `damage.ts` holds
 * it - the row reads that state rather than keeping a second copy of it.
 */
export function weaponRows(
    scene: WeaponScene, destroyed: ReadonlySet<string>,
): WeaponRow[] {
    // A hardpoint with no `Model_To_Attach` contributes no part - 137 of them do not - but it still
    // names fire bones, and those belong to the hull.
    const partOf = new Map(
        scene.hardpoints.map(h => [h.id, h.partId ?? 'hull'] as const));

    return scene.weapons.map(weapon => {
        const hardpointId = weapon.hardpointId ?? null;
        const partId = hardpointId === null ? 'hull' : partOf.get(hardpointId) ?? 'hull';

        return {
            id: weapon.id,
            name: hardpointId ?? weapon.label,
            label: weapon.label,
            source: weapon.source,
            hardpointId,
            partId,
            fireBones: [...weapon.fireBones],
            arcs: arcsOf(weapon, partId),
            cadence: cadenceText(weapon),
            damage: damageText(weapon),
            reach: reachText(weapon),
            cone: coneText(weapon),
            projectileId: weapon.projectileType ?? null,
            fireModes: [...weapon.fireModes],
            turret: weapon.turret ?? null,
            // A unit weapon belongs to the hull, which is the subject itself and is never in the
            // destroyed set - guarding on the source keeps it that way if a mount is ever named
            // after the hull part.
            destroyed: weapon.source === PREVIEW_WEAPON_SOURCE.hardpoint
                && hardpointId !== null && destroyed.has(hardpointId),
        };
    });
}

/**
 * The arcs the viewport should be showing right now.
 *
 * `master` is the stage pill and `hiddenBanks` the rows switched off; a destroyed mount drops out
 * regardless, because its model is hidden and a cone hanging in the gap would claim the wreck still
 * shoots.
 */
export function visibleArcs(
    rows: readonly WeaponRow[], master: boolean, hiddenBanks: ReadonlySet<string>,
): WeaponArc[] {
    if (!master) {
        return [];
    }

    return rows
        .filter(row => !row.destroyed && !hiddenBanks.has(row.id))
        .flatMap(row => row.arcs);
}

/**
 * Why a bank's tick is on offer, or why it is not.
 *
 * A disabled control has to say what would make it available - greying it out and leaving the
 * reader to guess is the failure the disable-don't-hide rule exists to avoid.
 */
export function bankTitle(row: WeaponRow): string {
    if (row.destroyed) {
        return 'This mount has been shot away, so it no longer fires';
    }

    return row.arcs.length > 0
        ? "Draw this bank's cone"
        : 'This weapon declares no range, so there is no cone to draw';
}

/** Same, for the fire-bone buttons: a bone off the mounted model is not in the hull's tree. */
export function fireBoneTitle(bone: string, inTree: boolean): string {
    return inTree
        ? `Select ${bone} on the model`
        : `${bone} is a bone of this mount's own model, not of the hull`;
}

/**
 * Which tree row a fire bone by that name is, so a row can point at one.
 *
 * The ONE place a weapon's bone name meets the model tree's identity, for the reason `boneIds.ts`
 * gives: every consumer that derived this for itself keyed one map by name and another by id, and
 * the lookup across them quietly matched nothing.
 *
 * A weapon names bones the way the XML writes them and the tree carries them the way the exporter
 * did, so the match is case-insensitive. A name the hull does not carry is simply absent - a
 * hardpoint's `FP_*` bone lives on the MOUNTED model, which is not in this tree, and the row's
 * button is disabled rather than selecting some other bone that happens to share the name.
 */
export function boneRowIndex(items: readonly TreeItem[]): Map<string, string> {
    const index = new Map<string, string>();

    for (const item of items) {
        // Skeleton order, first one wins. Two bones may share a name, and the reader clicking the
        // same button twice must land on the same one both times.
        if (item.kind === 'bone' && !index.has(item.name.toLowerCase())) {
            index.set(item.name.toLowerCase(), item.id);
        }
    }

    return index;
}

/**
 * One cone per fire bone, or none at all.
 *
 * Reach is the gate rather than the cone tags. A `Fires_Forward` weapon declares no traverse and
 * the zero-width cone collapses to a ray down the bone, which is the honest picture of it; without
 * a range there is no length to draw and the gizmo would be a point at the muzzle.
 */
function arcsOf(weapon: PreviewWeapon, partId: string): WeaponArc[] {
    const range = weapon.range ?? 0;
    if (range <= 0) {
        return [];
    }

    return weapon.fireBones.map(bone => ({
        weaponId: weapon.id,
        partId,
        bone,
        widthDegrees: weapon.coneWidthDegrees ?? 0,
        heightDegrees: weapon.coneHeightDegrees ?? 0,
        range,
    }));
}

function cadenceText(weapon: PreviewWeapon): string | null {
    const parts: string[] = [];
    const shots = weapon.pulseCount ?? null;
    const delay = weapon.pulseDelaySeconds ?? null;
    const recharge = weapon.rechargeSeconds ?? null;

    if (shots !== null) {
        parts.push(`${num(shots)} shot${shots === 1 ? '' : 's'}`);
    }

    // The gap between shots of one volley. With a single shot there is no gap, so a delay the data
    // happens to carry is not something to read out.
    if (delay !== null && (shots === null || shots > 1)) {
        parts.push(`${num(delay)}s apart`);
    }

    if (recharge !== null) {
        parts.push(`${num(recharge)}s recharge`);
    }

    return parts.length === 0 ? null : parts.join(', ');
}

function damageText(weapon: PreviewWeapon): string | null {
    const damage = weapon.damage ?? null;
    const type = weapon.damageType ?? null;

    if (damage === null) {
        return type;
    }

    return type === null ? `${num(damage)} damage` : `${num(damage)} damage, ${type}`;
}

function reachText(weapon: PreviewWeapon): string | null {
    const range = weapon.range ?? null;
    if (range === null) {
        return null;
    }

    // A minimum of zero is the default rather than a band - saying "0-2000" would invent a near
    // limit the weapon does not have.
    const min = weapon.minRange ?? 0;

    return min > 0 ? `${num(min)}-${num(range)} units` : `${num(range)} units`;
}

function coneText(weapon: PreviewWeapon): string | null {
    const width = weapon.coneWidthDegrees ?? null;
    const height = weapon.coneHeightDegrees ?? null;

    if (width === null && height === null) {
        return null;
    }

    return `${num(width ?? 0)} x ${num(height ?? 0)} deg`;
}

/** A number as a person writes it: the XML's `2.0000` is `2`, and `0.10` is `0.1`. */
function num(value: number): string {
    return Number.parseFloat(value.toFixed(4)).toString();
}

/**
 * Every bank that has a cone to draw.
 *
 * The subject opens with all of them OFF and the stage pill switches the lot on or off together -
 * the user's call, and the reason is measured: the Nebulon B's four mounts each declare 175 by 160
 * degrees, very nearly omnidirectional, so drawing them together fills the viewport however correct
 * the geometry is. Starting dark means the reader turns on exactly what they want to look at.
 *
 * Only the DRAWABLE ones. A weapon with no reach has no cone, so there is nothing to switch either
 * way, and listing it would make the pill's "n of m banks" count lie.
 */
export function allBankIds(rows: readonly WeaponRow[]): ReadonlySet<string> {
    return new Set(rows.filter(row => row.arcs.length > 0).map(row => row.id));
}
