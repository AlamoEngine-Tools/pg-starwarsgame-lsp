// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the tree row's eye shows, and whether pressing it can do anything.
//
// A tick could only ever say on or off, which is one bit short of the truth. A row can be undrawn
// for two quite different reasons, and the reader has to be able to tell them apart before they
// press anything:
//
//   - the ROW is why - the reader hid it, or the file, the level or a clip does. Pressing the eye
//     changes that, and it is exactly what someone reaches for on a hidden collision hull.
//   - something ABOVE it is why - a hidden ancestor, or a master toggle. Pressing the eye here can
//     do nothing at all: the chain vetoes this row before it ever reads its own state, so an
//     override would be stored and then ignored, and the control would look broken.
//
// So three states, which is Blender's arrangement and the reader asked for it by name: an open eye,
// a half-shut one for visibility inherited from above, and a closed one for a row that is off.
//
// The half eye is the DISABLED state - disable, don't hide. It is not a third thing to click
// through, and its title says only that the state is inherited: the row's own tooltip already
// carries `becauseText`, which names the ancestor. The way back to "the model decides" is in
// `setRow`, which stores the reader's word only where it disagrees with the model, so showing a
// mesh you had hidden gives the row back rather than pinning it the other way.

import { type VisibilityLink } from './visibility';

/** What the eye shows. */
export type EyeState = 'shown' | 'inherited' | 'hidden';

/** What the chain settled about one row, as the eye needs it. */
export interface EyeFacts {
    visible: boolean;
    decidedBy: VisibilityLink;
    /** What the row would do if the reader had said nothing - see `Resolution.authored`. */
    authored: boolean;
}

/** The eye on one row. */
export interface RowEye {
    state: EyeState;
    /** False when the decision was made above this row, so the button is disabled. */
    canAct: boolean;
    title: string;
    /** The row is drawn, or hidden, against what the model does - what the italics say. */
    againstModel: boolean;
}

/** The links that speak for something ABOVE the row rather than for the row itself. */
const FROM_ABOVE: readonly VisibilityLink[] = ['master', 'ancestor'];

/** How one row's eye reads. */
export function rowEye(facts: EyeFacts): RowEye {
    const fromAbove = FROM_ABOVE.includes(facts.decidedBy);
    const state: EyeState = facts.visible ? 'shown' : fromAbove ? 'inherited' : 'hidden';

    return {
        state,
        canAct: !fromAbove,
        title: titleFor(state),
        againstModel: facts.visible !== facts.authored,
    };
}

/**
 * What the eye promises, in words.
 *
 * Short. The row's own tooltip already carries `becauseText`, which names the ancestor or the
 * master, so spelling that out here says it twice - and a title is read at a glance or not at all.
 */
function titleFor(state: EyeState): string {
    if (state === 'inherited') {
        return 'State inherited from parent';
    }

    return state === 'shown'
        ? 'Hide this and everything under it'
        : 'Show this and everything under it';
}
