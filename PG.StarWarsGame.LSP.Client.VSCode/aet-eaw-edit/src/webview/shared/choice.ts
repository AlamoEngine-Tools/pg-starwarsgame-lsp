// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One of N, as a model rather than as a presentation.
//
// The extension picks a mode in two shapes: a row of joined buttons (ModeSelector) and a rotary dial
// (RotaryModeSwitch). They had an option type each, and the two agreed on almost everything - an id,
// a label, a glyph, a longer title, whether the option can be taken - while disagreeing on the parts
// that are genuinely about how the control is DRAWN.
//
// So this is the shared half, and the shapes keep their own files. That split is deliberate: the
// dial needs an angle per position and a fitted radius, and the row needs to express "no option
// holds right now". A single component with a `variant` prop would not have unified the call site -
// it would still demand an angle for one variant and reject it for the other, so the union would
// survive, just hidden one level down.

import { type IconName } from './Icon';

/**
 * One option in a one-of-N choice.
 *
 * The parts both shapes agree on. Availability sits below, deliberately apart.
 */
interface Choosable<Id extends string> {
    id: Id;

    /** What the option is called. Shown unless a glyph replaces it. */
    label: string;

    /**
     * A meaning from the icon catalogue, shown INSTEAD of the label.
     *
     * A meaning, not a library's glyph name: this used to be "a codicon name without the prefix",
     * which was the one hole left in the icon seam - and no call site ever used it.
     */
    icon?: IconName;

    /** Longer than the label, for the tooltip. */
    title?: string;
}

/**
 * One option, with its availability as plain optional fields.
 *
 * Deliberately NOT the {@link Enablement} union the buttons use, and the difference is worth
 * recording. That union is excellent where a control is authored as a literal - it makes a disabled
 * state without a reason fail to compile. It is hostile where the option is DATA that gets read
 * back: a camera entry is built by a function, stored, filtered and asserted on, and a union member
 * cannot be narrowed by an ordinary field read, so every consumer ends up writing `in` checks to
 * ask a yes-or-no question.
 *
 * So the rule is enforced where it is cheap - on Button and IconButton, which are always written
 * out at the call site - and stated here. ModeSelector shows the reason instead of the title when
 * an option is disabled, so an option that omits it degrades to naming itself rather than to
 * saying nothing.
 */
export type ChoiceOption<Id extends string> = Choosable<Id> & {
    /**
     * Offered but not choosable yet.
     *
     * Disabled rather than dropped: a choice that vanishes when unavailable takes its own
     * explanation with it - the reader never learns the option exists - and every option beside it
     * shifts under their eye.
     */
    disabled?: boolean;

    /** Why it cannot be chosen. Shown instead of the title, so it should still name the option. */
    disabledReason?: string;
};
