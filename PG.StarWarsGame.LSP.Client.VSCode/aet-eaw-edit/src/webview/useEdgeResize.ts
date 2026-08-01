// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Drag-to-resize for a panel edge, shared by the story graph and localisation webviews.
//
// Pointer capture rather than window listeners: the drag keeps following the pointer outside the
// handle, and there is nothing to unsubscribe if the component unmounts mid-drag.

import { PointerEvent as ReactPointerEvent, useRef, useState } from 'react';

/**
 * @param axis Which edge the handle sits on - 'e' and 'w' resize width, 'n' resizes height upward.
 * @param persist Called with each new size, so a caller can remember it across remounts.
 */
export function useEdgeResize(
    initial: number, min: number, max: number, axis: 'e' | 'w' | 'n', persist: (v: number) => void,
): { size: number; handleProps: Record<string, unknown> } {
    const [size, setSize] = useState(initial);
    const drag = useRef<{ start: number; base: number } | null>(null);
    const clamp = (v: number): number => Math.max(min, Math.min(max, v));
    const horizontal = axis === 'e' || axis === 'w';

    return {
        size,
        handleProps: {
            onPointerDown: (e: ReactPointerEvent<HTMLDivElement>) => {
                e.preventDefault();
                e.currentTarget.setPointerCapture(e.pointerId);
                drag.current = { start: horizontal ? e.clientX : e.clientY, base: size };
            },
            onPointerMove: (e: ReactPointerEvent<HTMLDivElement>) => {
                if (!drag.current) { return; }
                const delta = axis === 'e' ? e.clientX - drag.current.start
                    : axis === 'w' ? drag.current.start - e.clientX
                        : drag.current.start - e.clientY;
                const next = clamp(drag.current.base + delta);
                setSize(next);
                persist(next);
            },
            onPointerUp: () => { drag.current = null; },
        },
    };
}
