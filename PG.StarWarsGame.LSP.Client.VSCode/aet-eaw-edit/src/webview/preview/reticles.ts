// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The targeting marks the game draws over a hardpoint you can shoot at.
//
// HUD, not geometry. The engine sizes a reticle as a fraction of the screen and draws it over the
// hull rather than in it, so this is a projected overlay - the same shape as the bone labels, which
// is the precedent in this viewport - not a quad in the scene. That also settles occlusion without
// a depth trick: chrome is never occluded.
//
// What `0.03` is a fraction OF is nowhere in the data. Height is the reading taken here, because it
// keeps the mark the same size when the panel is made wider, which is how the rest of the game's
// HUD behaves. If it turns out to be width, this is the one function to change.

import type { PreviewHardpoint, PreviewReticleStates, PreviewReticles } from '../../protocol/modelPreview';

/** Which of the seven states a hardpoint is being shown in. */
export type ReticleState = keyof PreviewReticleStates;

/**
 * The states, in the order the dock offers them.
 *
 * Enemy first: it is what you see when you are looking at somebody else's ship, which is the case a
 * modder is checking. Every one is offered even though shipped data reuses the plain and tracked art
 * for the disabled pair - a mod giving them separate art has nowhere else to see it.
 */
export const RETICLE_STATES: readonly { id: ReticleState; label: string }[] = [
    { id: 'enemy', label: 'Enemy' },
    { id: 'enemyTracked', label: 'Enemy, tracked' },
    { id: 'friendly', label: 'Friendly' },
    { id: 'friendlyTracked', label: 'Friendly, tracked' },
    { id: 'friendlyRepairing', label: 'Repairing' },
    { id: 'friendlyDisabled', label: 'Disabled' },
    { id: 'friendlyDisabledTracked', label: 'Disabled, tracked' },
];

/** One mark the viewport projects and draws. */
export interface ReticleMark {
    hardpointId: string;
    /**
     * The part the ATTACHMENT BONE belongs to - the hull for a hardpoint whose bone is the hull's.
     *
     * The bone, not the attached model's centre. Two reasons, and the second is the one that bit:
     * the game draws the mark on the attachment bone, and centring on the part meant asking
     * three.js for a whole part's bounding box per mark per tick, which walks every vertex of the
     * geometry. Ten hardpoints at twelve ticks a second made the viewport crawl.
     */
    partId: string;
    /** The attachment bone to hang the mark on. */
    bone: string | null;
    /** The hardpoint's `Type`, for the tooltip. */
    type: string;
    iconUri: string;
    /**
     * The TRACKED art for the same hardpoint, shown while the pointer is over it.
     *
     * The game swaps to it when you put your cursor on a target, so hovering is the honest way to
     * see it - which is what replaced the seven-way dropdown that used to pick a state by hand.
     * Falls back to the plain art for a type that declares none.
     */
    trackedUri: string;
    /**
     * What is left of this hardpoint, 0 to 1, or null when it declares no health.
     *
     * The game colours the mark by it - see {@link healthColour} - so a reader can see at a glance
     * which hardpoints are nearly gone without reading a list.
     */
    healthFraction: number | null;
}

/**
 * The colour the game draws a reticle in, by how much of the hardpoint is left.
 *
 * Bright green at full health through yellow and orange to red at nothing - the user's own
 * description of the in-game ramp. A hardpoint that declares NO health is never anything but whole -
 * 210 of foc's hardpoints declare none - so it stays green rather than reading as destroyed.
 */
export function healthColour(fraction: number | null | undefined): string {
    if (fraction === null || fraction === undefined) {
        return RAMP[0][1];
    }

    const at = Math.min(1, Math.max(0, fraction));

    // Four colours, four EQUAL bands: green above three quarters, then yellow, orange and red,
    // switching at 75, 50 and 25 percent.
    //
    // It used to take the nearest of four stops written at 1, 0.66, 0.33 and 0, which put the
    // switches at 83, 50 and 17 percent - thresholds nobody can predict from looking at a bar, and
    // nothing in the game asks for them. Flat bands rather than a blend, though: the game's marks
    // are flat colours and interpolating would put one in a shade that never appears in it.
    const band = RAMP.find(([floor]) => at > floor);

    return (band ?? RAMP[RAMP.length - 1])[1];
}

/**
 * Health fraction to colour, healthiest first, each entry giving the FLOOR of its band.
 *
 * The colours are the user's - the game's own ramp. The thresholds are quarters, so the last entry
 * has to sit below zero: a hardpoint on exactly nothing must still be red, and a floor of 0 with a
 * strict `>` would fall off the end of the list.
 */
const RAMP: readonly (readonly [number, string])[] = [
    [0.75, '#3cd63c'],
    [0.5, '#d6d63c'],
    [0.25, '#e08a20'],
    [-1, '#e02020'],
];

/**
 * A mark per targetable hardpoint, in the state being shown.
 *
 * Anchored on the ATTACHMENT BONE, which is the hull's - that is where the game draws it. Centring
 * on the attached model's bounds instead was wrong AND slow: it asked three.js for a part's
 * bounding box per mark per tick, walking every vertex, and ten hardpoints made the viewport crawl.
 *
 * Absent art is a skip, never a broken image: the icons are empty whenever no game directory is
 * configured, and a scene reads perfectly well without reticles.
 */
export function reticleMarks(
    hardpoints: readonly PreviewHardpoint[],
    reticles: PreviewReticles | null | undefined,
    state: ReticleState,
    destroyed: ReadonlySet<string>,
    health: Readonly<Record<string, number | null>> = {},
): ReticleMark[] {
    if (reticles === null || reticles === undefined) {
        return [];
    }

    const marks: ReticleMark[] = [];

    for (const hardpoint of hardpoints) {
        // A wreck is not a target. Its model is hidden too, so a mark left behind would float in
        // the gap where the hardpoint used to be.
        if (!hardpoint.isTargetable || destroyed.has(hardpoint.id)) {
            continue;
        }

        const type = hardpoint.type ?? '';

        // EXACTLY as the hardpoint spells it. A case-insensitive scan would have papered over the
        // camel-cased keys the wire used to send, and left the real defect on the server.
        const states = Object.hasOwn(reticles.byType, type) ? reticles.byType[type] : undefined;
        const icon = states?.[state] ?? null;

        if (icon === null || !Object.hasOwn(reticles.icons, icon)) {
            continue;
        }

        // The tracked twin of whatever state is being shown, for the hover.
        const trackedIcon = states?.[trackedStateOf(state)] ?? null;
        const trackedUri = trackedIcon !== null && Object.hasOwn(reticles.icons, trackedIcon)
            ? reticles.icons[trackedIcon]
            : reticles.icons[icon];

        // The mark is attached to the ATTACHMENT BONE, which belongs to whatever the hardpoint is attached
        // TO - the hull, in every shipped case - not to the attached model.
        const left = health[hardpoint.id];
        const full = hardpoint.health ?? null;

        marks.push({
            hardpointId: hardpoint.id,
            partId: 'hull',
            bone: hardpoint.attachBone ?? null,
            type,
            iconUri: reticles.icons[icon],
            trackedUri,
            healthFraction: full === null || full <= 0 || left === null || left === undefined
                ? null
                : Math.min(1, Math.max(0, left / full)),
        });
    }

    return marks;
}

/**
 * The TRACKED twin of a state, which is what the game shows under the cursor.
 *
 * A state that has no tracked form - the tracked ones themselves - is its own twin, so hovering one
 * changes nothing rather than falling back to something unrelated.
 */
export function trackedStateOf(state: ReticleState): ReticleState {
    switch (state) {
        case 'enemy': return 'enemyTracked';
        case 'friendly': return 'friendlyTracked';
        case 'friendlyDisabled': return 'friendlyDisabledTracked';
        default: return state;
    }
}

/** Below this the mark stops reading as a reticle and starts reading as a speck. */
const MINIMUM_PX = 8;

/** A fallback height, for the frame before the canvas has been laid out. */
const ASSUMED_HEIGHT = 720;

/**
 * How many pixels across a mark should be drawn.
 *
 * Of the HEIGHT - see the note at the top of this file. The floor is not cosmetic: a mod may write
 * `0`, and a mark of no pixels is indistinguishable from one that failed to load.
 */
export function reticleSizePx(
    screenSize: number | null | undefined, _width: number, height: number,
): number {
    const usable = height > 0 ? height : ASSUMED_HEIGHT;
    const fraction = screenSize ?? 0;

    return Math.max(MINIMUM_PX, usable * fraction);
}

/** The size to use for one state: the friendly figure for the friendly states, enemy otherwise. */
export function reticleScreenSize(
    reticles: PreviewReticles | null | undefined, state: ReticleState,
): number | null | undefined {
    return state.startsWith('friendly')
        ? reticles?.friendlyScreenSize
        : reticles?.enemyScreenSize;
}

/**
 * The reticle art a hardpoint TYPE resolves to, ready to put in an img.
 *
 * Two levels, because the catalog has two: a type names an icon and an icon names the pixels -
 * thirteen shipped hardpoint types share five artwork families, so inlining the PNG per type would
 * send the same image up to four times.
 *
 * The enemy state, which is the one a reader is looking at when they think of a targeting mark.
 * Absent art is a quiet null: a workspace with no game directory configured gets the map and no
 * images, and a scene is perfectly usable without them.
 */
export function reticleFor(
    reticles: { byType?: Record<string, PreviewReticleStates>; icons?: Record<string, string> }
        | null | undefined,
    type: string | null | undefined,
): string | null {
    if (reticles === null || reticles === undefined || type === null || type === undefined) {
        return null;
    }

    const icon = reticles.byType?.[type]?.enemy ?? null;

    return icon === null ? null : reticles.icons?.[icon] ?? null;
}
