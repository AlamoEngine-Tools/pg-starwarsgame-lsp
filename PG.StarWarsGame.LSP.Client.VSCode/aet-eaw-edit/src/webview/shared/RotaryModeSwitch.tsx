// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The rotary mode switch: the active mode reads out large in the centre, the modes available arc
// above it like a half horizon, and clicking one rotates to it. The centre cycles.
//
// Extracted from the story graph, which had it first, once the model preview needed the same thing.
// Copying it would have made "the way this product switches modes" two independent implementations
// that drift; each editor supplies its own modes and keeps the behaviour.
//
// A mode keeps its ANGLE when others are hidden, so the one you know stays where your eye expects.

import * as l10n from "@vscode/l10n";
import * as React from "react";

import { type ChoiceOption } from "./choice";
import { Icon, type IconName } from "./Icon";

/**
 * One position on the dial.
 *
 * A choice option with a place on the arc. Everything a position shares with a joined-button option
 * - what it is called, what it is called at length, whether it can be taken - comes from the shared
 * model; the angle and the count are the parts that only mean something on a dial.
 */
export type RotaryMode<Id extends string> = Omit<ChoiceOption<Id>, "icon"> & {
    /**
     * The position's glyph, by meaning. Required here where a choice option's is optional: a dial
     * position has no room for a label. The dial meanings in Icon.tsx are drawn for a 24px circle.
     */
    icon: IconName;
    /** Degrees, 0 at 3 o'clock and 90 pointing down - so the arc above the centre is 180 to 360. */
    angle: number;
    /**
     * A count shown beside the label, for a mode whose usefulness depends on what the subject has.
     *
     * Absent means "not a counted thing". Zero is shown rather than hidden: "this unit has no
     * animations" is a finding worth surfacing in a preview whose job is to expose authoring gaps.
     */
    count?: number;
};

/**
 * How far the positions sit from the centre.
 *
 * The readout is 40px across with a 2px border and a position is 24px, so anything under 34 has the
 * two touching at the top of the arc. 40 leaves a clear gap all the way round, which is what makes
 * the arc read as an arc rather than as one lumpy shape.
 */
const RADIUS = 40;

export function RotaryModeSwitch<Id extends string>(props: {
    mode: Id;
    modes: readonly RotaryMode<Id>[];
    onSelect: (mode: Id) => void;
}): React.JSX.Element {
    const { modes } = props;
    const active = modes.find((mode) => mode.id === props.mode) ?? modes[0];
    const order = modes.map((mode) => mode.id);

    // Steps to the next mode that can actually be taken. Cycling blindly walked straight into a
    // disabled one, which is how a graph nothing could edit still ended up in Edit mode.
    const cycle = (): void => {
        const at = order.indexOf(props.mode);
        for (let step = 1; step <= order.length; step++) {
            const candidate = modes[(at + step) % order.length];
            if (candidate.disabled !== true) {
                props.onSelect(candidate.id);
                return;
            }
        }
    };

    return (
        <div className="rotary">
            {modes.map((mode) => {
                const radians = (mode.angle * Math.PI) / 180;
                const x = Math.cos(radians) * RADIUS;
                const y = Math.sin(radians) * RADIUS;

                return (
                    <button
                        key={mode.id}
                        className={
                            "rotary-pos" +
                            (mode.id === props.mode ? " active" : "")
                        }
                        style={{
                            transform: `translate(-50%, -50%) translate(${x}px, ${y}px)`,
                        }}
                        disabled={mode.disabled ?? false}
                        // The reason outranks the label: it is the only thing worth saying about a
                        // mode that cannot be entered, and saying it HERE is what stops someone
                        // working in a mode whose edits will be refused later.
                        title={
                            mode.disabled
                                ? (mode.disabledReason ?? mode.label)
                                : mode.count === undefined
                                  ? mode.label
                                  : `${mode.label} (${mode.count})`
                        }
                        onClick={() => props.onSelect(mode.id)}
                    >
                        <Icon name={mode.icon} />
                    </button>
                );
            })}
            <button
                className="rotary-center"
                title={
                    order.length > 1
                        ? l10n.t("{0} - click to switch mode", active.label)
                        : l10n.t(
                              "{0} - the other modes are unavailable here",
                              active.label,
                          )
                }
                onClick={cycle}
            >
                <Icon name={active.icon} />
            </button>
        </div>
    );
}
