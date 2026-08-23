// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which set of tools the dock is showing.
//
// Three LENSES on one subject, not three things you can open: Model is what the asset IS - geometry,
// materials, levels, colours - Animation is its clips, and Gameplay is what the game DOES to it,
// which is hardpoint destruction, death clones and the effects those drive.
//
// A particle FILE is not a lens. It has no meshes, no bones, no clips and no hardpoints, so every
// other lens would be empty; it gets a dock of its own instead, the way a graph editor is a
// different editor rather than a mode of the text one.
//
// The switch is SOFT. Changing lens never changes what is running or visible - an animation keeps
// playing while you hop into Model mode to hide a mesh - so each lens carries a one-line chip
// saying what the others are up to. Without that, a soft switch just hides state.

import type { RotaryMode } from '../shared/RotaryModeSwitch';

export type PreviewMode = 'model' | 'animation' | 'gameplay';

/** What the subject has to work with, for the counts on the dial. */
export interface SubjectCounts {
    animations: number;
    hardpoints: number;
    particles: number;
    /** Weapon banks, wherever declared - a fighter's guns are on the unit, not on a mount. */
    weapons: number;
    abilities: number;
}

/** What the modes you are NOT in are currently doing. */
export interface ModeActivity {
    /** The clip playing, or null when nothing is. */
    playing: string | null;
    destroyed: number;
    particleSystems: number;
    /** Abilities switched on, which are holding their proxies visible. */
    abilities: number;
}

/**
 * The three positions, in a fixed order and at fixed angles.
 *
 * Fixed so a mode stays where the eye expects it whatever the subject carries, and Model at the top
 * because it is where most work starts.
 */
export function previewModes(counts: SubjectCounts): RotaryMode<PreviewMode>[] {
    return [
        // Thin, simple glyphs. The positions are 24px circles and the readout a 40px one, so a
        // dense or already-round icon - `play-circle` inside a circle, `symbol-structure`'s grid -
        // fills its button edge to edge and the dial stops reading as a dial. The story graph's
        // own three (`eye`, `edit`, `play`) are the calibration.
        {
            id: 'animation', icon: 'play', label: 'Animation', angle: 210,
            count: counts.animations,
        },
        { id: 'model', icon: 'package', label: 'Model', angle: 270 },
        {
            // Everything the lens can act on, not only the mounts. It counted hardpoints alone
            // until the lens grew weapons and abilities, which read 0 on a fighter that carries its
            // guns on the unit itself - a dial position claiming a subject has nothing to see.
            id: 'gameplay', icon: 'zap', label: 'Gameplay', angle: 330,
            count: counts.hardpoints + counts.weapons + counts.abilities,
        },
    ];
}

/**
 * Where a subject opens.
 *
 * From what was opened rather than from what it turns out to contain: an `.ala` whose model has no
 * clips still opens in Animation mode, because the empty list is the answer to why nothing moves
 * and bouncing elsewhere would hide it.
 */
export function defaultMode(sceneKind: string, _counts: SubjectCounts): PreviewMode {
    return sceneKind === 'Animation' ? 'animation' : 'model';
}

/** One chip per other mode that has something running, in dial order. */
export function otherModeChips(
    mode: PreviewMode, activity: ModeActivity,
): { mode: PreviewMode; text: string }[] {
    const chips: { mode: PreviewMode; text: string }[] = [];

    if (mode !== 'animation' && activity.playing !== null) {
        chips.push({ mode: 'animation', text: `${activity.playing} playing` });
    }

    if (mode !== 'gameplay' && activity.destroyed > 0) {
        chips.push({
            mode: 'gameplay',
            text: `${activity.destroyed} hardpoint${activity.destroyed === 1 ? '' : 's'} destroyed`,
        });
    }

    // An active ability is holding proxies on. Without this the state is invisible from every other
    // lens, which is exactly what the chips exist to prevent.
    if (mode !== 'gameplay' && activity.abilities > 0) {
        chips.push({
            mode: 'gameplay',
            text: `${activity.abilities} abilit${activity.abilities === 1 ? 'y' : 'ies'} active`,
        });
    }

    return chips;
}
