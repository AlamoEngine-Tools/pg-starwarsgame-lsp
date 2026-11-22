// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One pick out of a list, as a pill on a stage plate.
//
// The pill holds the glyph of what is picked, the value, and the mark of a list behind a divider -
// the divider and the mark are what tell it from a toggle, which is a pill too. It is as wide as
// its value and no wider, which is the whole reason it exists: a slider with its value written
// beside it took 261px of a 538px slot for a number between 0 and 3.
//
// The LIST is the platform's own. An invisible `<select>` lies over the whole pill, so a press
// anywhere opens it, the keyboard works as on any select, a screen reader hears a select - and
// each row of the list can say more than the pill has room for. The pill shows `2`; its row says
// `2 (12 meshes, 1,507 tris)`. The platform also decides which way the list opens, by the room it
// finds, so the mark points both ways instead of promising one.
//
// For a SHORT ORDERED list on a plate that has no room for a slider. A long list in a form is a
// `SelectField`; one choice out of three that should all stay on screen is a `ModeSelector`.

import { type Enablement } from "./Button";
import { Icon, type IconName } from "./Icon";

export interface PillOption<T extends string | number> {
    value: T;
    /** The row of the list: the value and whatever else is worth saying about it. */
    label: string;
}

export type PillSelectProps<T extends string | number> = Enablement & {
    /** What is being picked, as a glyph. */
    icon: IconName;

    value: T;

    /** What the pill shows for the value. The value itself when not given. */
    shown?: string;

    options: readonly PillOption<T>[];

    /**
     * The tooltip and the list's accessible name: what is picked and what it currently is, since
     * the pill itself has room for the value alone.
     */
    title: string;

    /** A placement class for the host. Added to the base class, never replacing it. */
    className?: string;

    onPick: (value: T) => void;
};

export function PillSelect<T extends string | number>({
    icon,
    value,
    shown,
    options,
    title,
    className,
    disabled,
    disabledReason,
    onPick,
}: PillSelectProps<T>): React.JSX.Element {
    // The reason REPLACES the title, as on a button: see `Enablement`.
    const says = disabled ? disabledReason : title;

    return (
        <label
            className={["pill-select", className, disabled ? "disabled" : null]
                .filter(Boolean)
                .join(" ")}
            title={says}
        >
            <Icon name={icon} size={14} />
            <span className="pill-value">{shown ?? String(value)}</span>
            <span className="pill-mark" aria-hidden="true">
                <Icon name="picker" size={12} />
            </span>
            <select
                className="pill-list"
                aria-label={says}
                value={String(value)}
                disabled={disabled}
                onChange={(event) => {
                    const picked = options.find(
                        (option) => String(option.value) === event.target.value,
                    );
                    if (picked !== undefined) {
                        onPick(picked.value);
                    }
                }}
            >
                {options.map((option) => (
                    <option
                        key={String(option.value)}
                        value={String(option.value)}
                    >
                        {option.label}
                    </option>
                ))}
            </select>
        </label>
    );
}
