// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Validation results, as a dismissable bar across the foot of the table.
//
// The same shape the story graph editor uses: draggable from its top edge, closeable, and each row
// takes you to what it is complaining about. The dock is the wrong home for these - it is narrow,
// so every message was truncated, and a list that appears and disappears there shifts the controls
// above it.

import { useEdgeResize } from '../useEdgeResize';
import { LocProblem } from './useLocPanel';

// Module-level so a remount keeps the height the user dragged it to.
let problemsHeightMemo = 120;

export function LocProblemsBar(props: {
    problems: LocProblem[];
    /** Renders the entry a problem names - a key or a row number, depending on the editor. */
    labelOf: (problem: LocProblem) => string;
    /** Null when the problem names nothing that can be shown, such as a whole-batch failure. */
    jumpTo: (problem: LocProblem) => (() => void) | null;
    onClose: () => void;
}): React.JSX.Element {
    const { size: height, handleProps } = useEdgeResize(
        problemsHeightMemo, 60, 420, 'n', v => { problemsHeightMemo = v; });

    const errors = props.problems.filter(p => p.severity === 'error').length;

    return (
        <div className="loc-problems" style={{ height }}>
            <div className="resize-handle-n" title="Drag to resize" {...handleProps} />
            <div className="panel-bar">
                <span className="panel-title">
                    Problems ({props.problems.length}{errors > 0 && errors < props.problems.length
                        ? `, ${errors} errors` : ''})
                </span>
                <button className="panel-close" onClick={props.onClose} title="Close">
                    <span className="codicon codicon-close" />
                </button>
            </div>

            <div className="problem-list">
                {props.problems.map((problem, i) => {
                    const jump = props.jumpTo(problem);
                    return (
                        <div
                            key={i}
                            className={`problem-row${jump ? ' clickable' : ''}`}
                            title={jump ? 'Click to show this entry in the table' : undefined}
                            onClick={jump ?? undefined}
                        >
                            <span
                                className={`codicon codicon-${
                                    problem.severity === 'error' ? 'error'
                                        : problem.severity === 'info' ? 'info' : 'warning'
                                } sev-${problem.severity}`}
                                aria-hidden="true"
                            />
                            <span className="problem-target">{props.labelOf(problem)}</span>
                            <span className="problem-msg" title={problem.message}>
                                {problem.message}
                            </span>
                        </div>
                    );
                })}
            </div>
        </div>
    );
}
