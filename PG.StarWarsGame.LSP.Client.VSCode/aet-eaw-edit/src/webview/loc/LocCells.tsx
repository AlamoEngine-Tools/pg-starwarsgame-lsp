// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Grid pieces both editors use. Neither knows what a row means; they are about typing into a cell
// and about a menu staying where it was opened.

import { ReactNode, useEffect, useLayoutEffect, useRef, useState } from 'react';

import { MenuPlacement, placeMenu } from './menuPlacement';

/**
 * A cell that commits on blur or Enter and reverts on Escape.
 *
 * Local state while editing, so typing does not restage a command per keystroke - the queue would
 * fill with intermediate values and every render would rebuild the whole row list.
 */
export function CellInput(props: {
    value: string;
    onCommit: (next: string) => void;
}): React.JSX.Element {
    const [draft, setDraft] = useState(props.value);
    const committed = useRef(props.value);

    useEffect(() => {
        setDraft(props.value);
        committed.current = props.value;
    }, [props.value]);

    const commit = (): void => {
        if (draft !== committed.current) {
            committed.current = draft;
            props.onCommit(draft);
        }
    };

    return (
        <input
            type="text"
            value={draft}
            onChange={e => setDraft(e.target.value)}
            onBlur={commit}
            onKeyDown={e => {
                if (e.key === 'Enter') { e.currentTarget.blur(); }
                if (e.key === 'Escape') { setDraft(committed.current); e.currentTarget.blur(); }
            }}
        />
    );
}

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
    children: ReactNode;
}): React.JSX.Element {
    const { anchorIndex, onClose } = props;

    const menuRef = useRef<HTMLDivElement>(null);
    const [placement, setPlacement] = useState<MenuPlacement>(
        { left: props.x, top: props.y, maxHeight: null });

    // Measured rather than estimated, because what the menu contains differs by row - a spacer
    // offers different items from an entry. A layout effect runs before paint, so the corrected
    // position is the first one drawn and the menu never visibly jumps.
    //
    // Keyed on the anchor, never on the children: the child elements are a fresh object every
    // render, so depending on them re-runs this after each reposition, which never settles.
    const { x, y } = props;
    useLayoutEffect(() => {
        const el = menuRef.current;
        if (el === null) { return; }

        const box = el.getBoundingClientRect();
        const next = placeMenu(
            { x, y },
            { width: box.width, height: box.height },
            { width: window.innerWidth, height: window.innerHeight });

        setPlacement(current => (current.left === next.left && current.top === next.top
            && current.maxHeight === next.maxHeight
            ? current
            : next));
    }, [x, y, anchorIndex]);

    useEffect(() => {
        const close = (): void => onClose();
        const onKey = (e: KeyboardEvent): void => { if (e.key === 'Escape') { onClose(); } };

        // Closing on any scroll at all is too blunt. Right-clicking a row while the caret sits in
        // another one blurs that cell, and the browser scrolls the grid horizontally by a few
        // pixels putting it back - which fired a scroll event a frame after the menu opened and
        // closed it again. What matters is whether the menu still points at its row, so the row's
        // own vertical position is what is watched: a real scroll moves it, a blur nudge does not.
        const anchorTop = (): number | null => {
            const row = document.querySelector(`[data-row-index="${anchorIndex}"]`);
            return row === null ? null : row.getBoundingClientRect().top;
        };
        const openedAt = anchorTop();
        const onScroll = (): void => {
            const now = anchorTop();
            // Gone entirely means scrolled out of the virtualised window - close.
            if (now === null || openedAt === null || Math.abs(now - openedAt) > 2) { onClose(); }
        };

        window.addEventListener('pointerdown', close);
        window.addEventListener('scroll', onScroll, true);
        window.addEventListener('keydown', onKey);
        return () => {
            window.removeEventListener('pointerdown', close);
            window.removeEventListener('scroll', onScroll, true);
            window.removeEventListener('keydown', onKey);
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
                overflowY: placement.maxHeight === null ? undefined : 'auto',
            }}
            onPointerDown={e => e.stopPropagation()}
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
