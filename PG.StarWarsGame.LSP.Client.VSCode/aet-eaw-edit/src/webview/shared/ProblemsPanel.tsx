// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The dismissable bar across the foot of an editor that validation results appear in.
//
// Only the frame is shared, not the rows. The two editors report genuinely different things - the
// story graph names a node and offers to open the XML behind it, the localisation grids name an
// entry and take you to it - and flattening those into one component would mean a props bag with
// half its fields unused on either side. What they do have in common is everything around the
// rows: draggable from its top edge, a counted title, a close button, and remembering the height
// it was dragged to. That had been written twice, down to the class names.

import { useEdgeResize } from '../useEdgeResize';
import { readPanelSize, writePanelSize } from './panelLayout';

/** What the panel needs to draw and work the view filter. */
export interface ProblemFilterControl {
    /** How many the filter excludes, shown or not. */
    hidden: number;
    /** Whether the excluded ones are currently being shown anyway. */
    showingAll: boolean;
    /** Whether there is anything to toggle - see `FilteredProblems.filterable`. */
    filterable: boolean;
    onToggle: () => void;
}

function filterTitle(filter: ProblemFilterControl): string {
    if (!filter.filterable) {
        return 'Every problem is in view, so there is nothing to filter.';
    }

    return filter.showingAll
        ? `Showing all problems. ${filter.hidden} are not in the current view.`
        : `Showing only problems in the current view. ${filter.hidden} hidden.`;
}



export function ProblemsPanel(props: {
    /** The editor's own class for this bar; each skins it in its own stylesheet. */
    className: string;
    /** Distinguishes this panel's remembered height from the other's. */
    memoKey: string;
    defaultHeight: number;
    /** The counted heading, e.g. `Problems (3, 1 error)`. */
    title: string;
    onClose: () => void;
    /**
     * The view filter, for an editor that has one.
     *
     * Optional on purpose: every problems table will want this eventually, but each has to answer
     * "what is in view" for itself first, and one that has no answer yet passes nothing and renders
     * exactly as it always did. See `filterProblems`.
     */
    filter?: ProblemFilterControl;
    children: React.ReactNode;
}): React.JSX.Element {
    // Closing and reopening the bar is not a request to forget how tall it was, and neither is
    // closing the tab: the size now outlives the window, keyed so the two panels never shove each
    // other around.
    const { size: height, handleProps } = useEdgeResize(
        readPanelSize(props.memoKey, props.defaultHeight), 60, 420, 'n',
        value => { writePanelSize(props.memoKey, value); });

    return (
        <div className={props.className} style={{ height }}>
            <div className="resize-handle-n" title="Drag to resize" {...handleProps} />
            <div className="panel-bar">
                <span className="panel-title">{props.title}</span>

                {/* Beside the count it qualifies. Disabled rather than absent when the filter is
                    holding nothing back: a control that comes and goes is one nobody learns is
                    there, and its title still explains what it would do. */}
                {props.filter !== undefined && (
                    <button
                        // Active means actually holding something back, not merely "not showing
                        // all" - otherwise a filter with nothing to exclude draws itself lit up
                        // while disabled, which reads as a filter that is on and stuck.
                        className={'panel-filter'
                            + (!props.filter.showingAll && props.filter.filterable ? ' active' : '')}
                        disabled={!props.filter.filterable}
                        aria-pressed={!props.filter.showingAll}
                        onClick={props.filter.onToggle}
                        title={filterTitle(props.filter)}
                    >
                        <span className={'codicon codicon-filter'
                            + (!props.filter.showingAll && props.filter.filterable
                                ? '-filled' : '')} />
                        {props.filter.hidden > 0 && !props.filter.showingAll
                            ? ` ${props.filter.hidden} hidden`
                            : ''}
                    </button>
                )}

                <button className="panel-close" onClick={props.onClose} title="Close">
                    <span className="codicon codicon-close" />
                </button>
            </div>
            {props.children}
        </div>
    );
}
