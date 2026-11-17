// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A control that advances time, in one shape whichever editor it is in: the preview's animation
// player and the story simulator's tick transport, and whatever plays next. The styles are
// `playerCss`.
//
// The shape is the part that is shared - a CD player's row of buttons with the clock at its far
// end, something under the row in the scrubber's slot, then the speed. Which buttons, what the
// clock reads and what fills the middle belong to the editor: a clip has a timeline to scrub, a
// story run has a status to read out.

import * as l10n from "@vscode/l10n";
import { ReactNode } from "react";

import { Field } from "./Field";

export interface TransportBarProps {
    /** A variant such as `sim-transport`. Added after the base class, never replacing it. */
    className?: string;
    /** The row of buttons, in a player's order. */
    controls: ReactNode;
    /** The time, at the row's far end. Omitted where nothing has a timeline. */
    clock?: ReactNode;
    /** Under the row, in the scrubber's slot: the scrubber, or a readout standing in for it. */
    middle?: ReactNode;
    /** The speed: what it reads, and the slider that sets it. */
    speed: { readout: ReactNode; slider: ReactNode };
}

export function TransportBar({
    className,
    controls,
    clock,
    middle,
    speed,
}: TransportBarProps): React.JSX.Element {
    return (
        <div
            className={
                className === undefined ? "player" : `player ${className}`
            }
        >
            <div className="player-row">
                {controls}
                {clock !== undefined && (
                    <span className="player-time">{clock}</span>
                )}
            </div>
            {middle}
            <Field label={l10n.t("Speed")} value={speed.readout}>
                {speed.slider}
            </Field>
        </div>
    );
}
