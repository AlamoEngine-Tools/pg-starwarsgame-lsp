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



export function ProblemsPanel(props: {
    /** The editor's own class for this bar; each skins it in its own stylesheet. */
    className: string;
    /** Distinguishes this panel's remembered height from the other's. */
    memoKey: string;
    defaultHeight: number;
    /** The counted heading, e.g. `Problems (3, 1 error)`. */
    title: string;
    onClose: () => void;
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
                <button className="panel-close" onClick={props.onClose} title="Close">
                    <span className="codicon codicon-close" />
                </button>
            </div>
            {props.children}
        </div>
    );
}
