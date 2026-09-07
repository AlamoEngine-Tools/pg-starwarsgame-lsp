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

import * as React from 'react';

import { type ChoiceOption } from './choice';

/**
 * One position on the dial.
 *
 * A choice option with a place on the arc. Everything a position shares with a joined-button option
 * - what it is called, what it is called at length, whether it can be taken - comes from the shared
 * model; the angle and the count are the parts that only mean something on a dial.
 *
 * The icon is the one field that does NOT come from there, and deliberately. It is a codicon name
 * rather than a meaning from the catalogue because these glyphs are calibrated as a set: the
 * positions are 24px circles inside a 40px readout, and previewMode.ts records that a dense or
 * already-round glyph fills its button edge to edge and the dial stops reading as a dial. Moving
 * them to the catalogue means choosing six new glyphs against that constraint, which is a look
 * decision rather than a refactor - so it is the last hole left in the icon seam, and it is open on
 * purpose until someone picks them.
 */
export type RotaryMode<Id extends string> = Omit<ChoiceOption<Id>, 'icon'> & {
    /** A codicon name, without the `codicon-` prefix. See the note above. */
    icon: string;
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
    const active = modes.find(mode => mode.id === props.mode) ?? modes[0];
    const order = modes.map(mode => mode.id);

    const cycle = (): void => {
        const at = order.indexOf(props.mode);
        props.onSelect(order[(at + 1) % order.length]);
    };

    return (
        <div className="rotary">
            {modes.map(mode => {
                const radians = (mode.angle * Math.PI) / 180;
                const x = Math.cos(radians) * RADIUS;
                const y = Math.sin(radians) * RADIUS;

                return (
                    <button
                        key={mode.id}
                        className={'rotary-pos' + (mode.id === props.mode ? ' active' : '')}
                        style={{ transform: `translate(-50%, -50%) translate(${x}px, ${y}px)` }}
                        title={mode.count === undefined
                            ? mode.label
                            : `${mode.label} (${mode.count})`}
                        onClick={() => props.onSelect(mode.id)}
                    ><span className={'codicon codicon-' + mode.icon} /></button>
                );
            })}
            <button
                className="rotary-center"
                title={order.length > 1
                    ? `${active.label} - click to switch mode`
                    : `${active.label} - the other modes are unavailable here`}
                onClick={cycle}
            >
                <span className={'codicon codicon-' + active.icon} />
            </button>
        </div>
    );
}
