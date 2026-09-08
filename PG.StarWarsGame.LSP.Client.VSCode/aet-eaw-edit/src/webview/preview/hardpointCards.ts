// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One card per hardpoint, joining what the Hardpoints list held to what the Weapons list held.
//
// They were two lists describing the same thing from opposite ends. The Hardpoints list carried the
// health and the destroy tick; the Weapons list carried the arc tick and the fire bones - so
// blowing a hardpoint off and drawing its cone were two sections apart, and neither list showed the
// game's own words for it at all: the Type that picks its reticle, and the tooltip a player reads.
//
// What is left over is the weapons without hardpoints - a fighter's guns are on the unit - and
// those keep a list of their own, because putting them under Hardpoints would be a lie.

import { type PreviewHardpoint, type PreviewTurret } from '../../protocol/modelPreview';
import { healthColour } from './reticles';
import { type WeaponRow, type WeaponScene, weaponRows } from './weaponRows';

/** One hardpoint, with everything the card shows. */
export interface HardpointCard {
    /** The XML `Name`, which is the card's title and what a jump-to-definition resolves. */
    id: string;

    /** Its `Type` - the thing that picks its reticle, and what the tooltip's key is built from. */
    type: string | null;

    /**
     * What the game tells a PLAYER this is, resolved from its localisation key by the server.
     *
     * Null where the key resolves to nothing - which is an authoring mistake, but the localisation
     * editor's to report. Echoing the key here would hide it behind something that looks like text.
     */
    tooltip: string | null;

    /** The key the author wrote, shown beside the text so both are searchable. */
    tooltipKey: string | null;

    attachBone: string | null;
    health: number | null;
    isDestroyable: boolean;
    isTargetable: boolean;

    /** Whether the reader has blown it off. Read from the same set `damage.ts` holds. */
    destroyed: boolean;

    /** The weapon it carries, or null: 137 shipped hardpoints carry none. */
    weapon: WeaponRow | null;

    /** Its traverse, from the hardpoint or from the weapon on it - see {@link sweepable}. */
    turret: PreviewTurret | null;

    /**
     * Whether swinging it through its traverse would show anything.
     *
     * The sweep used to be one button acting on every turret at once, which on a hull whose
     * hardpoints declare different extents is a control that cannot say what it will do.
     */
    sweepable: boolean;
}

/** Whether a turret declares a traverse worth swinging through. */
function hasTraverse(turret: PreviewTurret | null | undefined): boolean {
    return (turret?.rotateExtentDegrees ?? 0) > 0 || (turret?.elevateExtentDegrees ?? 0) > 0;
}

/**
 * A card per hardpoint, in the order the scene lists them.
 *
 * `destroyed` is the set of ids the reader has blown off, exactly as `damage.ts` holds it, so the
 * card reads that state rather than keeping a second copy that can disagree with it.
 */
export function hardpointCards(
    scene: WeaponScene, destroyed: ReadonlySet<string>,
): HardpointCard[] {
    const rows = weaponRows(scene, destroyed);
    const byHardpoint = new Map(
        rows.filter(row => row.hardpointId !== null).map(row => [row.hardpointId!, row] as const));

    return scene.hardpoints.map((hardpoint: PreviewHardpoint) => {
        const weapon = byHardpoint.get(hardpoint.id) ?? null;

        // The hardpoint's own turret first, then the weapon's. The AT-AA declares its traverse on
        // the weapon and has no hardpoints at all, so reading only the hardpoint would call a
        // swinging turret fixed.
        const turret = hasTraverse(hardpoint.turret)
            ? hardpoint.turret ?? null
            : weapon?.turret ?? null;

        return {
            id: hardpoint.id,
            type: hardpoint.type ?? null,
            tooltip: hardpoint.tooltipText ?? null,
            tooltipKey: hardpoint.tooltipKey ?? null,
            attachBone: hardpoint.attachBone ?? null,
            health: hardpoint.health ?? null,
            isDestroyable: hardpoint.isDestroyable,
            isTargetable: hardpoint.isTargetable,
            destroyed: destroyed.has(hardpoint.id),
            weapon,
            turret,
            sweepable: hasTraverse(turret),
        };
    });
}

/**
 * The weapons without hardpoints, which is what the Weapons section is left holding.
 *
 * A fighter carries its guns on the unit itself and declares no hardpoints, so this is not an edge
 * case - it is every fighter in the game.
 */
export function unitWeapons(rows: readonly WeaponRow[]): WeaponRow[] {
    return rows.filter(row => row.hardpointId === null);
}

/**
 * The short facts under a card's title, with nothing the file did not declare.
 *
 * Omission rather than a printed null or a zero: 145 shipped hardpoints declare only `Health` and
 * some declare neither that nor a bone, and a row of dashes says less than no row at all.
 */
export function hardpointFacts(card: HardpointCard): string[] {
    // NOT the health. `healthBar` draws that on the face of the card, numbers and all, and saying
    // it again three lines below is the same fact twice.
    const facts: string[] = [];

    if (!card.isDestroyable) {
        facts.push('indestructible');
    }

    if (card.attachBone !== null) {
        facts.push(`on ${card.attachBone}`);
    }

    // Degrees as the XML writes them, not halved into a per-side figure. `Turret_Rotate_Extent_
    // Degrees` is the full swing, and a reader checking the panel against their file needs the
    // number that is in it.
    if ((card.turret?.rotateExtentDegrees ?? 0) > 0) {
        facts.push(`swings ${card.turret?.rotateExtentDegrees} deg`);
    }

    if ((card.turret?.elevateExtentDegrees ?? 0) > 0) {
        facts.push(`elevates ${card.turret?.elevateExtentDegrees} deg`);
    }

    return facts;
}

/**
 * The short facts under a UNIT weapon card's title.
 *
 * {@link hardpointFacts} minus the two that cannot apply. A unit weapon is not a TARGET: it is
 * neither destructible nor indestructible, it declares no pool, and it hangs off the hull rather
 * than an attachment bone of its own. What is left is the turret - which a unit weapon can very much
 * declare, and the AT-AA is the reason to care: its turret is on the WEAPON and it has no hardpoints
 * at all, so a control that lives only on a hardpoint card can never reach it.
 *
 * Degrees as the XML writes them, exactly as the hardpoint's do, so a reader checking the panel
 * against their file sees the number that is in it.
 */
export function unitWeaponFacts(weapon: WeaponRow): string[] {
    const facts: string[] = [];

    if ((weapon.turret?.rotateExtentDegrees ?? 0) > 0) {
        facts.push(`swings ${weapon.turret?.rotateExtentDegrees} deg`);
    }

    if ((weapon.turret?.elevateExtentDegrees ?? 0) > 0) {
        facts.push(`elevates ${weapon.turret?.elevateExtentDegrees} deg`);
    }

    return facts;
}

/** A hardpoint's health, ready to draw as a bar. */
export interface HealthBar {
    /** 0 to 1, clamped: a pool cannot be more than full or less than empty. */
    fraction: number;

    /** `12/400 HP` - the numbers, because a bar alone cannot be checked against a file. */
    label: string;

    colour: string;

    /** Shot away. Drawn grey rather than red - see below. */
    destroyed: boolean;
}

/**
 * The bar under a card's title, or null when the hardpoint declares no pool to draw.
 *
 * 97 shipped hardpoints are indestructible and many declare no `Health` at all; an empty bar there
 * would claim a pool that does not exist.
 *
 * The colour is {@link healthColour}, the same ramp the targeting marks on the model use - so a
 * mark that has gone orange out there and a bar that has gone orange in here are saying the same
 * thing, which is the whole reason to share it.
 *
 * Destroyed is GREY, not red. Red means "nearly dead and still fighting"; a hardpoint that is gone
 * is not on the ramp at all, and colouring it like the worst live state says otherwise.
 */
export function healthBar(card: HardpointCard, live: number | null): HealthBar | null {
    if (card.health === null || card.health <= 0) {
        return null;
    }

    const left = card.destroyed ? 0 : Math.min(card.health, Math.max(0, live ?? card.health));
    const fraction = left / card.health;

    return {
        fraction,
        label: `${Math.round(left)}/${Math.round(card.health)} HP`,
        colour: card.destroyed ? DESTROYED_COLOUR : healthColour(fraction),
        destroyed: card.destroyed,
    };
}

/** What a hardpoint that is no longer there looks like. Grey, and it comes from the theme. */
const DESTROYED_COLOUR = 'var(--vscode-disabledForeground, #5a5a5a)';

/**
 * Which fire-bone slot a bone sits in, by position.
 *
 * `Fire_Bone_A` and `Fire_Bone_B`, in that order - the two the engine reads. The card showed the
 * bone name alone, which says where a shot leaves from but not which slot declared it, and the two
 * are different questions when a hardpoint fills only one of them.
 *
 * Kept going past B rather than repeating a letter: a mod can list more bones than the engine
 * reads, and two rows both called Muzzle B would be worse than an honest C.
 */
export function muzzleLabel(index: number): string {
    return `Muzzle ${String.fromCharCode(65 + index)}`;
}

/**
 * What a weapon measures, as labelled rows for the info panel.
 *
 * A LIST rather than the joined sentence this used to be. On the face of a card it ran to three
 * wrapped lines each - "1100 units - 175 x 160 deg - 5 shots, 0.2s apart, 3s recharge - ..." - and
 * five hardpoints of that is a wall of prose nobody reads a number out of.
 *
 * Pairs, not an object, because the ORDER is part of it: reach first because it is the number most
 * often being checked, then the shape of the cone, then how fast it fires, then what it fires.
 */
export function weaponFacts(weapon: WeaponRow): [string, string][] {
    const rows: [string, string | null][] = [
        ['Reach', weapon.reach],
        ['Cone', weapon.cone],
        ['Cadence', weapon.cadence],
        ['Damage', weapon.damage],
        ['Projectile', weapon.projectileId],
        ['Fires when', weapon.fireModes.length > 0 ? weapon.fireModes.join(', ') : null],
    ];

    return rows.filter((row): row is [string, string] =>
        row[1] !== null && row[1] !== undefined && row[1] !== '');
}
