// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One setting: what it is called, what it currently reads, the control itself, and the sentence
// under it that says what it does.
//
// The single most repeated shape in the extension - .field appears in nine files and sixty-five
// times in the preview alone - and until now it was assembled by hand at every one of them. That is
// why a readout could be forgotten and the pane read as a set of typos ("Wind speed1.0", "Light
// around45 deg"), and why the explanatory sentence went six settings without any rule at all and
// rendered at full body weight.
//
// The parts are ordered here rather than at the call site, which is what makes them consistent: the
// explanation belongs ON the label, because it explains the setting rather than the control; the
// readout goes to the far end of the label, because it is a value someone reads a number off; and
// the note goes UNDER the control, because it is support text and the control is the point.

import { ReactNode } from 'react';

import { type BadgeSeverity } from './badges';
import { InfoBadge } from './InfoBadge';

export interface FieldProps {
    label: ReactNode;

    /**
     * The explanation behind the mark on the label.
     *
     * A tooltip rather than printed text: under every control it turns a panel of eight settings
     * into a page of prose, and the setting someone came for ends up three scrolls down.
     */
    info?: ReactNode;

    /**
     * What the mark on the label is saying: an explanation, or that something is off.
     *
     * Colouring the mark rather than the sentence is what lets a panel be scanned for problems
     * without reading a word, so a note that reports something wrong has to say so here.
     */
    infoSeverity?: BadgeSeverity;

    /** What the control currently reads. Sent to the far end of the label. */
    value?: ReactNode;

    /**
     * What the readout means, where a derived number has to explain itself.
     *
     * On the NUMBER, not on the label: the label's own mark describes the setting, and this
     * describes where the value came from. Two different questions, so two different tooltips.
     */
    valueTitle?: string;

    /** The sentence under the control. Support text, not content. */
    note?: ReactNode;

    /**
     * The note is reporting that something is off rather than explaining the control.
     *
     * Colour and nothing else: it has to stay the same kind of text in the same place, or the panel
     * jumps the moment something goes wrong and the sentence becomes an alert the eye has to deal
     * with on every glance.
     */
    warn?: boolean;

    /**
     * `label` makes the whole field the control's label, so clicking its name reaches the control.
     * That is the better default where it applies, and it only applies to a field holding exactly
     * ONE control: a label containing two belongs to neither, and one containing a button swallows
     * the press. Hence `div` as the default - it is the shape that is always correct.
     */
    as?: 'div' | 'label';

    /**
     * The control sits BESIDE its name rather than under it.
     *
     * For the small ones - a colour well, a checkbox - where a line of their own wastes the row.
     * The note stays under the pair either way: it describes the setting, not the row.
     */
    inline?: boolean;

    /**
     * The control.
     *
     * Optional, because a checkbox IS its own label: it lives inside `label` so that pressing the
     * word toggles it, and there is then nothing left to sit underneath.
     */
    children?: ReactNode;
}

export function Field({
    label, info, infoSeverity, value, valueTitle, note, warn, as = 'div', inline,
    children,
}: FieldProps): React.JSX.Element {
    const Box = as;

    const named = (
        <>
            <span className="field-label">
                {label}
                {info !== undefined && <InfoBadge severity={infoSeverity}>{info}</InfoBadge>}
                {value !== undefined && (
                    <span className="section-count" title={valueTitle}>{value}</span>
                )}
            </span>

            {children}
        </>
    );

    return (
        <Box className="field">
            {inline ? <span className="view-row">{named}</span> : named}

            {note !== undefined && (
                <p className={warn ? 'field-warn' : 'field-note'}>{note}</p>
            )}
        </Box>
    );
}
