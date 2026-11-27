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

import * as l10n from "@vscode/l10n";
import { ReactNode, useState } from "react";

import { IconButton } from "./Button";
import { useEdgeResize } from "./useEdgeResize";
import { readPanelSize, writePanelSize } from "../../shared/panelLayout";

export interface RightDockProps {
    /**
     * Pinned above the scrolling content: the dial and the document's passive state. What the page
     * does to its document - save, close, the verdict - is in the page toolbar, not here.
     */
    header?: ReactNode;
    /**
     * Whether the header carries the rotary mode dial, which needs a taller header. Held from the
     * first frame, before the dial itself has arrived, so the dock does not draw itself in two
     * steps. A header that holds the dial can be folded away by the chevron in its corner; the dock
     * remembers that with its width, under the same `memoKey`.
     */
    headerHoldsDial?: boolean;
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

    /**
     * Identifies this dock's remembered width, as `<surface>.dock`. Each editor keeps its own -
     * the story graph's palette and the preview's inspector are different questions.
     *
     * Omit it and the dock still works; it just forgets, which is what every editor did before.
     */
    memoKey?: string;

    initialWidth?: number;
    minWidth?: number;
    maxWidth?: number;
    /** Called with each new width, for a caller that wants to react to the drag itself. */
    onWidthChange?: (width: number) => void;
}

export function RightDock({
    header,
    headerHoldsDial = false,
    content,
    overview,
    memoKey,
    initialWidth = 260,
    minWidth = 180,
    maxWidth = 560,
    onWidthChange,
}: RightDockProps): React.JSX.Element {
    // 'w': dragging left grows a dock pinned to the right edge.
    const { size, handleProps } = useEdgeResize(
        memoKey === undefined
            ? initialWidth
            : readPanelSize(memoKey, initialWidth),
        minWidth,
        maxWidth,
        "w",
        (width) => {
            if (memoKey !== undefined) {
                writePanelSize(memoKey, width);
            }
            onWidthChange?.(width);
        },
    );

    // A fold is kept with the dock's sizes, as a size of one: it follows the reader like the width
    // does, and each editor's dock keeps its own.
    const foldKey =
        memoKey === undefined ? undefined : `${memoKey}.headerFolded`;
    const [folded, setFolded] = useState(
        foldKey !== undefined && readPanelSize(foldKey, 0) === 1,
    );
    const toggleFold = (): void => {
        const next = !folded;
        setFolded(next);
        if (foldKey !== undefined) {
            writePanelSize(foldKey, next ? 1 : 0);
        }
    };

    return (
        <div className="right-dock" style={{ width: size }}>
            <div
                className="resize-handle-w"
                title={l10n.t("Drag to resize")}
                {...handleProps}
            />
            {header !== undefined && (
                <div
                    className={
                        "dock-header" +
                        (headerHoldsDial ? " with-dial" : "") +
                        (headerHoldsDial && folded ? " folded" : "")
                    }
                >
                    {headerHoldsDial && (
                        <IconButton
                            icon={folded ? "unfoldDown" : "foldUp"}
                            className="header-fold"
                            size={13}
                            title={
                                folded
                                    ? l10n.t("Open the dock header")
                                    : l10n.t("Fold the dock header away")
                            }
                            expanded={!folded}
                            onClick={toggleFold}
                        />
                    )}
                    {/* Unmounted while folded, like a folded section's body. */}
                    {headerHoldsDial && folded ? null : header}
                </div>
            )}
            <div className="dock-content">{content}</div>
            {overview !== undefined && (
                <div className="dock-overview">{overview}</div>
            )}
        </div>
    );
}
