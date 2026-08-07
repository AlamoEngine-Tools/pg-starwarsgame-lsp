// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Drag-to-move and drag-to-resize for a dialog, shared by every modal in both webviews.
//
// Pointer capture rather than window listeners, for the same reason useEdgeResize uses it: the drag
// keeps following the pointer outside the handle, and there is nothing to unsubscribe if the dialog
// closes mid-drag.
//
// A dialog stays centred by layout until it is first moved or resized, or until it is reopened
// somewhere it was previously put. Only then does it take an explicit position, so a dialog that has
// never been moved is untouched by any of this.

import { CSSProperties, PointerEvent as ReactPointerEvent, useCallback, useRef, useState } from 'react';

import { rememberedGeometry, rememberGeometry } from './dialogGeometryStore';
import {
    centredPosition, clampPosition, fromStoredGeometry, Point, Rect, resizeRect, ResizeDirection,
    Size, toStoredGeometry,
} from './modalGeometry';

/** Every edge and corner, in the order the handles are rendered. */
export const RESIZE_DIRECTIONS: readonly ResizeDirection[] =
    ['n', 's', 'e', 'w', 'ne', 'nw', 'se', 'sw'];

export interface MovableDialog {
    /** Spread onto the dialog element. Carries no style until it has been moved or resized. */
    dialogProps: { ref: (el: HTMLElement | null) => void; style?: CSSProperties };
    /** Spread onto the title bar. */
    dragHandleProps: Record<string, unknown>;
    /** Spread onto the handle for one edge or corner. */
    resizeHandleProps: (direction: ResizeDirection) => Record<string, unknown>;
}

type DragState =
    | { kind: 'move'; pointerX: number; pointerY: number; base: Rect }
    | { kind: 'resize'; pointerX: number; pointerY: number; base: Rect; direction: ResizeDirection };

/**
 * @param dialogId Identifies the dialog across sessions, so it reopens where it was left. Omit it
 *     for a dialog whose placement is not worth remembering; it then always opens centred.
 */
export function useMovableDialog(dialogId?: string): MovableDialog {
    // Restored on the first render rather than in an effect, so a remembered dialog is painted where
    // it belongs instead of appearing centred and then jumping.
    const [rect, setRect] = useState<Rect | null>(() => {
        if (dialogId === undefined) { return null; }

        const stored = rememberedGeometry(dialogId);
        return stored === undefined
            ? null
            : fromStoredGeometry(stored, { width: window.innerWidth, height: window.innerHeight });
    });

    const element = useRef<HTMLElement | null>(null);
    const drag = useRef<DragState | null>(null);

    const viewport = (): { width: number; height: number } =>
        ({ width: window.innerWidth, height: window.innerHeight });

    /**
     * The dialog's current geometry, measured when a drag starts.
     *
     * Until then it is wherever the centring layout put it, at whatever size its content needed -
     * so both have to be read off the element, or the first drag would jump.
     */
    const measure = useCallback((): Rect => {
        if (rect !== null) { return rect; }

        const bounds = element.current?.getBoundingClientRect();
        if (!bounds) {
            const empty: Size = { width: 0, height: 0 };
            return { ...centredPosition(empty, viewport()), ...empty };
        }

        return { x: bounds.left, y: bounds.top, width: bounds.width, height: bounds.height };
    }, [rect]);

    const begin = useCallback((
        e: ReactPointerEvent<HTMLElement>, state: (base: Rect) => DragState,
    ) => {
        e.preventDefault();
        e.stopPropagation();
        e.currentTarget.setPointerCapture(e.pointerId);

        const base = measure();
        setRect(base);
        drag.current = state(base);
    }, [measure]);

    const onPointerDownMove = useCallback((e: ReactPointerEvent<HTMLElement>) => {
        // Not from a control inside the title bar - a close button there must still be clickable.
        if ((e.target as HTMLElement).closest('button, input, select, textarea, a')) { return; }

        begin(e, base => ({ kind: 'move', pointerX: e.clientX, pointerY: e.clientY, base }));
    }, [begin]);

    const onPointerMove = useCallback((e: ReactPointerEvent<HTMLElement>) => {
        const state = drag.current;
        if (state === null) { return; }

        const dx = e.clientX - state.pointerX;
        const dy = e.clientY - state.pointerY;

        if (state.kind === 'move') {
            const moved = clampPosition(
                { x: state.base.x + dx, y: state.base.y + dy }, state.base, viewport());
            setRect({ ...state.base, ...moved });
            return;
        }

        setRect(resizeRect(state.base, state.direction, dx, dy, viewport()));
    }, []);

    const endDrag = useCallback((e: ReactPointerEvent<HTMLElement>) => {
        const wasDragging = drag.current !== null;
        drag.current = null;
        if (e.currentTarget.hasPointerCapture(e.pointerId)) {
            e.currentTarget.releasePointerCapture(e.pointerId);
        }

        // Where it ended up is the decision worth keeping; everywhere it passed through is not.
        if (wasDragging && dialogId !== undefined) {
            setRect(current => {
                if (current !== null) {
                    rememberGeometry(dialogId, toStoredGeometry(
                        current, { width: window.innerWidth, height: window.innerHeight }));
                }
                return current;
            });
        }
    }, [dialogId]);

    const dragging = { onPointerMove, onPointerUp: endDrag, onPointerCancel: endDrag };

    return {
        dialogProps: {
            ref: (el: HTMLElement | null) => { element.current = el; },
            style: rect === null
                ? undefined
                : {
                    position: 'fixed',
                    left: rect.x,
                    top: rect.y,
                    width: rect.width,
                    height: rect.height,
                    // The centring layout and the content-driven bounds all have to stop applying.
                    // The caps would override an explicit size the moment it exceeded them, and the
                    // floors are worse: a dialog whose CSS min-width outranks MIN_MODAL_SIZE stops
                    // following the pointer while the hook goes on believing it shrank.
                    margin: 0,
                    minWidth: 0,
                    minHeight: 0,
                    maxWidth: 'none',
                    maxHeight: 'none',
                },
        },
        dragHandleProps: { onPointerDown: onPointerDownMove, ...dragging },
        resizeHandleProps: (direction: ResizeDirection) => ({
            onPointerDown: (e: ReactPointerEvent<HTMLElement>) =>
                begin(e, base => ({
                    kind: 'resize', pointerX: e.clientX, pointerY: e.clientY, base, direction,
                })),
            ...dragging,
        }),
    };
}
