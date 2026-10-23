// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A row's right-click menu, for every panel with rows: the localisation grids and the Mod Browser.
// Its look is .row-menu in dockChromeCss; its placement is placeMenu.

import { ReactNode, useEffect, useLayoutEffect, useRef, useState } from "react";

import { MenuPlacement, placeMenu } from "./menuPlacement";
import { menuStep } from "./rovingFocus";

/**
 * The frame a row's right-click menu sits in: position, and the three ways it goes away.
 *
 * Shared because dismissal is where the subtle bug was, and having it in one place means it cannot
 * be fixed in one editor and left broken in the other. The items themselves are supplied by the
 * editor, since what a row can do is exactly what the two disagree about.
 */
export function RowMenuFrame(props: {
    x: number;
    y: number;
    /** The row the menu was opened on, watched so the menu closes if it scrolls away. */
    anchorIndex: number;
    onClose: () => void;
    /**
     * Takes focus on its first enabled item, for a menu opened from the keyboard or from a row that
     * has nothing being edited. Off in the loc grids, where it would blur the cell being typed in.
     */
    focusFirst?: boolean;
    children: ReactNode;
}): React.JSX.Element {
    const { anchorIndex, onClose, focusFirst } = props;

    const menuRef = useRef<HTMLDivElement>(null);
    const items = (): HTMLButtonElement[] => [
        ...(menuRef.current?.querySelectorAll<HTMLButtonElement>(
            '[role="menuitem"]',
        ) ?? []),
    ];

    useEffect(() => {
        if (focusFirst) {
            items()
                .find((b) => !b.disabled)
                ?.focus();
        }
    }, [focusFirst]);

    // The arrows walk the enabled items and wrap, Home and End go to either end, as in the
    // editor's own menus.
    const onKeyDown = (e: React.KeyboardEvent<HTMLDivElement>): void => {
        const all = items();
        const to = menuStep(
            all.map((b) => !b.disabled),
            all.indexOf(document.activeElement as HTMLButtonElement),
            e.key,
        );
        if (to !== undefined) {
            e.preventDefault();
            all[to].focus();
        }
    };
    const [placement, setPlacement] = useState<MenuPlacement>({
        left: props.x,
        top: props.y,
        maxHeight: null,
    });

    // Measured rather than estimated, because what the menu contains differs by row - a spacer
    // offers different items from an entry. A layout effect runs before paint, so the corrected
    // position is the first one drawn and the menu never visibly jumps.
    //
    // Keyed on the anchor, never on the children: the child elements are a fresh object every
    // render, so depending on them re-runs this after each reposition, which never settles.
    const { x, y } = props;
    useLayoutEffect(() => {
        const el = menuRef.current;
        if (el === null) {
            return;
        }

        const box = el.getBoundingClientRect();
        const next = placeMenu(
            { x, y },
            { width: box.width, height: box.height },
            { width: window.innerWidth, height: window.innerHeight },
        );

        setPlacement((current) =>
            current.left === next.left &&
            current.top === next.top &&
            current.maxHeight === next.maxHeight
                ? current
                : next,
        );
    }, [x, y, anchorIndex]);

    useEffect(() => {
        const close = (): void => onClose();
        const onKey = (e: KeyboardEvent): void => {
            if (e.key === "Escape") {
                onClose();
            }
        };

        // Closing on any scroll at all is too blunt. Right-clicking a row while the caret sits in
        // another one blurs that cell, and the browser scrolls the grid horizontally by a few
        // pixels putting it back - which fired a scroll event a frame after the menu opened and
        // closed it again. What matters is whether the menu still points at its row, so the row's
        // own vertical position is what is watched: a real scroll moves it, a blur nudge does not.
        const anchorTop = (): number | null => {
            const row = document.querySelector(
                `[data-row-index="${anchorIndex}"]`,
            );
            return row === null ? null : row.getBoundingClientRect().top;
        };
        const openedAt = anchorTop();
        const onScroll = (): void => {
            const now = anchorTop();
            // Gone entirely means scrolled out of the virtualised window - close.
            if (
                now === null ||
                openedAt === null ||
                Math.abs(now - openedAt) > 2
            ) {
                onClose();
            }
        };

        window.addEventListener("pointerdown", close);
        window.addEventListener("scroll", onScroll, true);
        window.addEventListener("keydown", onKey);
        return () => {
            window.removeEventListener("pointerdown", close);
            window.removeEventListener("scroll", onScroll, true);
            window.removeEventListener("keydown", onKey);
        };
    }, [anchorIndex, onClose]);

    return (
        <div
            ref={menuRef}
            className="row-menu"
            style={{
                left: placement.left,
                top: placement.top,
                maxHeight: placement.maxHeight ?? undefined,
                overflowY: placement.maxHeight === null ? undefined : "auto",
            }}
            onPointerDown={(e) => e.stopPropagation()}
            onKeyDown={onKeyDown}
            role="menu"
        >
            {props.children}
        </div>
    );
}

/** Runs a menu item's action and closes the menu, without the click reaching the dismissal handler. */
export function menuAction(action: () => void, close: () => void) {
    return (e: React.MouseEvent): void => {
        e.stopPropagation();
        action();
        close();
    };
}
