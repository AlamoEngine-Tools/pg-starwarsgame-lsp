// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The dock's title row: Save pinned left, Validate a soft severity pill pinned right, both
// icon-first - laid out like the story graph editor's, which is where the shape came from.
//
// Shared because the two editors had it byte-for-byte identical, and because it reads out state
// (how many edits are staged, how the last validation went) that must be phrased the same way in
// both or the same situation looks like two different ones.

import { LocProblem, ValidationState } from './useLocPanel';
import { severityIconFor, validateTitle } from './validateState';

export function LocDockHeader(props: {
    /** The staged queue; its length is the badge on Save and disables the button when empty. */
    queue: readonly unknown[];
    problems: readonly LocProblem[];
    validation: ValidationState;
    onSave: () => void;
    onValidate: () => void;
}): React.JSX.Element {
    return (
        <>
            <button
                className={`icon-btn header-left${props.queue.length > 0 ? ' active' : ''}`}
                disabled={props.queue.length === 0}
                onClick={props.onSave}
                title="Save - write all staged changes to the file"
            >
                <span className="codicon codicon-save" />
                {props.queue.length > 0 ? ` ${props.queue.length}` : ''}
            </button>
            <button
                className={`icon-btn validate-btn header-right sev-${props.validation}`}
                onClick={props.onValidate}
                title={validateTitle(props.validation, props.problems.length, props.queue.length)}
            >
                <span className={`codicon codicon-${severityIconFor(props.validation)}`} />
                {props.problems.length ? ` ${props.problems.length}` : ''}
            </button>
        </>
    );
}
