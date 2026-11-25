// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Places a details flyout beside the dock, at the pill that opened it, and keeps it there.
//
// The pill lives inside the dock's scroller and the flyout outside it, so nothing but geometry
// joins the two: both are read at the moment of placing, and read again whenever the window is
// resized or anything scrolls, so the flyout follows its pill rather than pointing at where the
// pill used to be. The arithmetic is `anchorFlyout`.

import {
    type RefObject,
    useCallback,
    useEffect,
    useLayoutEffect,
    useState,
} from "react";

import { anchorFlyout, type AnchorPlacement } from "./flyoutAnchor";

function same(a: AnchorPlacement | null, b: AnchorPlacement | null): boolean {
    return (
        a === b ||
        (a !== null &&
            b !== null &&
            a.left === b.left &&
            a.top === b.top &&
            a.tail === b.tail)
    );
}

/**
 * Where the flyout in `box` goes, for the pill `anchor` selects - or null while there is no such
 * pill in a dock, or the box is not drawn yet. Null hides the flyout: a pill that a filter took
 * away has nothing left to point at.
 */
export function useFlyoutBesideDock(
    anchor: string | null,
    box: RefObject<HTMLElement | null>,
): AnchorPlacement | null {
    const [at, setAt] = useState<AnchorPlacement | null>(null);

    const place = useCallback((): void => {
        const pill = anchor === null ? null : document.querySelector(anchor);
        const dock = pill?.closest(".right-dock") ?? null;
        const flyout = box.current;

        if (pill === null || dock === null || flyout === null) {
            setAt((was) => (was === null ? was : null));
            return;
        }

        const row = pill.getBoundingClientRect();
        const next = anchorFlyout({
            row: { top: row.top, bottom: row.bottom },
            dockLeft: dock.getBoundingClientRect().left,
            window: { width: window.innerWidth, height: window.innerHeight },
            size: { width: flyout.offsetWidth, height: flyout.offsetHeight },
        });

        // The same answer is the same object, or placing after every render would render again.
        setAt((was) => (same(was, next) ? was : next));
    }, [anchor, box]);

    // Before the browser paints, and after EVERY render: what the flyout holds decides how tall it
    // is, and a taller box may have to move up to stay in the window.
    useLayoutEffect(() => {
        place();
    });

    useEffect(() => {
        if (anchor === null) {
            return;
        }

        // `capture` catches the dock's own scroller, not only the window's.
        window.addEventListener("resize", place);
        window.addEventListener("scroll", place, true);

        return () => {
            window.removeEventListener("resize", place);
            window.removeEventListener("scroll", place, true);
        };
    }, [anchor, place]);

    return at;
}
