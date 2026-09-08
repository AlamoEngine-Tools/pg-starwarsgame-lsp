// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A pressable control, with a label or with only a glyph.
//
// Both were written out by hand at every call site - twenty-one icon buttons and nine labelled ones
// in the preview alone - which is how three of the nine compact paddings in this codebase came to
// exist. They are one concept and live in one file: an icon button is a button whose label is a
// picture, and the rule below applies to it more sharply rather than less.
//
// One rule, enforced by the TYPE rather than by a review comment: disable, don't hide, and say why.
// A control with nothing to act on stays on screen, because the reader needs to see that the choice
// exists - but a disabled control that gives no reason is worse than a missing one, since the
// reader is left to guess what would make it work. `disabled` therefore cannot be written without
// `disabledReason`, and the reason REPLACES the title: the title of an unusable control describes
// something it cannot currently do.
//
// A good reason still NAMES the control, though - "Damage log - nothing fired yet", not "Nothing
// fired yet". A tooltip giving only the reason leaves a reader who has never opened the thing with
// no idea what it was going to be.

import { ReactNode } from 'react';

import { Icon, type IconName } from './Icon';

/**
 * Either the control always acts, or it can be disabled AND says why.
 *
 * A union rather than two optional props, so that a disabled state without a reason does not
 * compile. It discriminates on whether `disabled` is passed at all rather than on its value,
 * because in practice it is always a runtime expression - `disabled={animation === null}` - which
 * TypeScript cannot narrow to a literal. So the rule is: mention `disabled` and you owe a reason.
 */
export type Enablement =
    | { disabled?: undefined; disabledReason?: never }
    | { disabled: boolean; disabledReason: string };

interface Pressable {
    /**
     * The event is passed through because a control inside a row usually has to stop it: a row eye
     * that lets its click reach the row selects the row it was trying to hide.
     */
    onClick: (event: React.MouseEvent<HTMLButtonElement>) => void;
}

export type ButtonProps = Omit<Pressable, 'onClick'> & Enablement & {
    /**
     * What pressing it does. Optional only because a submit button has nothing of its own to
     * do - the form it sits in handles the press.
     */
    onClick?: (event: React.MouseEvent<HTMLButtonElement>) => void;

    /**
     * More than the label says, where there is more to say. Optional, unlike on an icon button:
     * the label already names this control, and "Cancel" titled "Cancel" is noise.
     */
    title?: string;

    /** Tighter padding, for a button sharing a row with others. */
    compact?: boolean;

    /** A variant such as `primary`. Added to the base class, never replacing it. */
    className?: string;

    /**
     * This button submits the form it is in.
     *
     * Everything defaults to type="button" on purpose - a stray submit is how a menu entry ends up
     * reloading the page - so the one control that IS the submit has to say so. Enter in a text
     * field then confirms the dialog, which is what a reader expects of one.
     */
    submit?: boolean;

    children: ReactNode;
};

export function Button({
    title, onClick, disabled, disabledReason, compact, className, submit, children,
}: ButtonProps): React.JSX.Element {
    return (
        <button
            type={submit ? 'submit' : 'button'}
            className={['btn', compact ? 'compact' : null, className].filter(Boolean).join(' ')}
            title={disabled ? disabledReason : title}
            disabled={disabled}
            onClick={onClick}
        >
            {children}
        </button>
    );
}

export type IconButtonProps = Pressable & Enablement & {
    icon: IconName;

    /**
     * Required here, unlike on a labelled button: there is no text to fall back on, so this
     * normally names the control to a reader hovering it and to a screen reader both.
     */
    title: string;

    /**
     * A shorter accessible name, where the title is a sentence.
     *
     * "Choose which language columns to show" is the right tooltip and the wrong thing to hear read
     * aloud before every other control on the row; that button wants to be called "Columns". Only
     * worth giving when the two genuinely differ - by default the title does both jobs.
     */
    label?: string;

    /** A placement or state variant such as `header-right`. Added to the base class, never replacing it. */
    className?: string;

    size?: number;

    /**
     * This control has a state rather than performing a one-off - it is a toggle.
     *
     * Given here rather than written as `aria-pressed` at each call site, because a toggle that
     * does not report its state is indistinguishable from an action to anything that is not looking
     * at the screen.
     */
    pressed?: boolean;

    /** This control opens something, and that thing is currently open. */
    expanded?: boolean;

    /**
     * A count beside the glyph - staged edits waiting to be saved, problems waiting to be read.
     *
     * Part of the button rather than a sibling, because it belongs to the same hit area: pressing
     * the number has to do what pressing the glyph does.
     */
    badge?: ReactNode;

    /** For a control something has to measure - a menu positioned under the button that opens it. */
    ref?: React.Ref<HTMLButtonElement>;
};

export function IconButton({
    icon, title, label, onClick, disabled, disabledReason, className, size, pressed, expanded,
    badge, ref,
}: IconButtonProps): React.JSX.Element {
    // There is no text to fall back on, so one string has to name the control. The reason it cannot
    // be pressed outranks both: it is the only thing worth saying while the control is dead.
    const hover = disabled ? disabledReason : title;
    const spoken = disabled ? disabledReason : (label ?? title);

    return (
        <button
            ref={ref}
            type="button"
            className={className === undefined ? 'icon-btn' : `icon-btn ${className}`}
            title={hover}
            aria-label={spoken}
            aria-pressed={pressed}
            aria-expanded={expanded}
            disabled={disabled}
            onClick={onClick}
        >
            <Icon name={icon} size={size} />
            {badge}
        </button>
    );
}
