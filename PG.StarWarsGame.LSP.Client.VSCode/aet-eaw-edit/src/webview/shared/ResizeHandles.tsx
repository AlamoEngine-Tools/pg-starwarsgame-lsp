// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { ResizeDirection } from './modalGeometry';
import { RESIZE_DIRECTIONS } from './useMovableDialog';

export interface ResizeHandlesProps {
    handleProps: (direction: ResizeDirection) => Record<string, unknown>;
}

/**
 * The eight grips around a dialog: four edges and four corners.
 *
 * Every edge and corner, not just the bottom-right, because that is what a window does everywhere
 * else on the desktop - a dialog you can only widen rightwards is one you fight. They are absolutely
 * positioned strips inside the padding, so they cost nothing in layout and never sit over the
 * content.
 *
 * The class is namespaced: the dock's width sash already owns a bare `resize-handle`, and sharing
 * the name let its rule flatten all eight of these onto the left edge.
 */
export function ResizeHandles(props: ResizeHandlesProps): React.JSX.Element {
    return (
        <>
            {RESIZE_DIRECTIONS.map(direction => (
                <div
                    key={direction}
                    className={`modal-resize modal-resize-${direction}`}
                    aria-hidden="true"
                    {...props.handleProps(direction)}
                />
            ))}
        </>
    );
}
