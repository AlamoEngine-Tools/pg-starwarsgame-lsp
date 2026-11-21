// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A one-of-N choice, as joined buttons rather than as a dropdown.
//
// The dock is full of settings where exactly one option can hold at a time - which way the camera
// looks, what is behind the model, which light the dials are editing - and a `<select>` hides every
// option but the chosen one behind a click. Three or four short labels fit on one row, so the whole
// choice reads at a glance and switching is one press instead of two.
//
// Not for long lists. A `<select>` is still right for a model's forty animation clips; this is for
// the handful where seeing the alternatives IS the point.
//
// A radiogroup, and that is not decoration: three independent toggles each reporting their own
// pressed state say nothing about being alternatives to one another, which is the one fact about
// this control worth telling a screen reader.

import * as React from "react";

import { type ChoiceOption } from "./choice";
import { Icon } from "./Icon";

export function ModeSelector<Id extends string>(props: {
    /**
     * The chosen option, or null for none.
     *
     * A preset can be departed from: dragging the camera off a preset view leaves it on no preset
     * at all, and the honest readout for that is an empty group rather than a highlight on a view
     * you are no longer looking from.
     */
    value: Id | null;
    options: readonly ChoiceOption<Id>[];
    onSelect: (id: Id) => void;
    /** Announced to assistive tech, since the group itself carries the meaning. */
    label: string;
    /**
     * The whole choice has nothing to act on - a skeleton choice over a file with no bones. Every
     * option is disabled and carries `disabledReason`, the chosen one too: the choice stays on
     * show, and one reason for the group says it once rather than an option at a time.
     */
    disabled?: boolean;
    disabledReason?: string;
}): React.JSX.Element {
    return (
        <span
            className="mode-selector"
            role="radiogroup"
            aria-label={props.label}
            aria-disabled={props.disabled === true ? true : undefined}
        >
            {props.options.map((option) => {
                // The group's reason first: it is why NONE can be taken, which outranks why one can't.
                const reason =
                    props.disabled === true
                        ? props.disabledReason
                        : option.disabled
                          ? option.disabledReason
                          : undefined;

                return (
                    <button
                        key={option.id}
                        type="button"
                        role="radio"
                        aria-checked={option.id === props.value}
                        className={option.id === props.value ? "active" : ""}
                        disabled={
                            props.disabled === true ||
                            (option.disabled ?? false)
                        }
                        // The reason outranks both: it is the only thing worth saying about an
                        // option that cannot be taken.
                        title={
                            props.disabled === true || option.disabled
                                ? reason
                                : (option.title ?? option.label)
                        }
                        onClick={() => props.onSelect(option.id)}
                    >
                        {option.icon === undefined ? (
                            option.label
                        ) : (
                            <Icon name={option.icon} />
                        )}
                    </button>
                );
            })}
        </span>
    );
}
