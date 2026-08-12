// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The right-hand tool dock, as a component.
//
// Tools live on the right in this extension, and the shape has been settled for a while: a drag
// handle on the left edge, an optional header, a scrolling content column, and an optional strip
// at the foot. What had NOT been settled is who owns that markup - the story graph and the
// localisation grid each wrote their own, and they had already drifted: one calls its container
// `.right-dock` and the other `.dock`, one gives its handle a hover state and the other a
// z-index. Only `useEdgeResize` and the class names were actually shared.
//
// This is the one definition. `dockChrome.ts` supplies the styles it expects; a consumer
// interpolates `rightDockCss` (and `dockChromeCss` for the sections inside it).

import { ReactNode } from 'react';

import { useEdgeResize } from '../useEdgeResize';

export interface RightDockProps {
    /** Pinned above the scrolling content - mode switches, save/validate, whatever the editor needs. */
    header?: ReactNode;
    /**
     * The dock body. Wrap groups in `.dock-section` with a `.dock-section-title`.
     *
     * A prop rather than `children`, matching `LocGridShell` - and because the three slots then
     * appear in source in the order they are drawn, instead of the overview having to be hoisted
     * above the content it sits below.
     */
    content: ReactNode;
    /** A strip at the foot, below the scroll - filters and overviews live here. */
    overview?: ReactNode;

    initialWidth?: number;
    minWidth?: number;
    maxWidth?: number;
    /**
     * Called with each new width. Webviews are remounted when their tab is restored, so a caller
     * that wants the dock to keep its size across that has to remember it somewhere outside React.
     */
    onWidthChange?: (width: number) => void;
}

export function RightDock({
    header, content, overview,
    initialWidth = 260, minWidth = 180, maxWidth = 560, onWidthChange,
}: RightDockProps): React.JSX.Element {
    // 'w': dragging left grows a dock pinned to the right edge.
    const { size, handleProps } = useEdgeResize(
        initialWidth, minWidth, maxWidth, 'w', onWidthChange ?? (() => { /* not persisted */ }));

    return (
        <div className="right-dock" style={{ width: size }}>
            <div className="resize-handle-w" title="Drag to resize" {...handleProps} />
            {header !== undefined && <div className="dock-header">{header}</div>}
            <div className="dock-content">{content}</div>
            {overview !== undefined && <div className="dock-overview">{overview}</div>}
        </div>
    );
}
