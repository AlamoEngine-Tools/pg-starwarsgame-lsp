// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A control's explanation, behind a mark on its label.
//
// The text is worth having - it is the difference between a slider someone drags at random and one
// they understand. Printed under every control it is also what turns a panel of eight settings into
// a page of prose, so the setting you came for is three scrolls down.
//
// It is a TOOLTIP, and that word is load-bearing: showing it must not move anything on screen. An
// earlier build unfolded the note in place, which pushed every control below it down the panel -
// reading about a slider shoved the slider itself somewhere else. This draws over the page instead
// and the layout never changes.
//
// It is OUR tooltip rather than the browser's `title`, because the native one cannot be styled, is
// slow to appear, wraps where it likes and vanishes while you read it.
//
// The mark carries severity. Most notes explain; some report that something is off - a light below
// the ground plane, a model that binds to no camera rule - and those matter early and stop
// mattering once dealt with. Colouring the mark rather than the sentence means the panel can be
// scanned for problems without reading a word.

import { ReactNode, useCallback, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { createGlobalStyle } from 'styled-components';

import { badgeIcon, type BadgeSeverity } from './badges';
import { Icon } from './Icon';
import { placeTooltip, type TooltipPlacement } from './tooltipPlace';
import { tokensRootCss } from './tokens';

/**
 * The bubble's styles, GLOBAL rather than part of the dock's shared block.
 *
 * That block is interpolated into a `styled.div`, so every rule in it is scoped to that element -
 * and the bubble is portalled to the document body, outside it. Written there the rules matched
 * nothing and the tooltip came out as a full-width unstyled block at the foot of the page, which is
 * exactly what an unstyled div looks like and nothing like a missing stylesheet.
 */
const TooltipStyles = createGlobalStyle`
    /* The tooltip is portalled to the body, so it is NOT inside any panel's Shell and inherits none
       of the custom properties interpolated there. Without this the four token references below
       resolve to nothing - and a padding that resolves to nothing is zero, not a fallback. */
    ${tokensRootCss}

    /* Our own tooltip rather than the browser's, which cannot be styled, is slow to appear and
       vanishes while you read it.

       Nothing here participates in layout. Showing an explanation must not move the control it
       explains - which is exactly what an earlier build did by unfolding the note in place. */
    .info-tip {
        position: fixed;
        z-index: 40;
        max-width: 290px;
        padding: var(--space-6) var(--space-8);
        border-radius: var(--radius-6);
        border: var(--space-1) solid var(--vscode-editorHoverWidget-border, rgba(128, 128, 128, 0.35));
        background: var(--vscode-editorHoverWidget-background, #252526);
        color: var(--vscode-editorHoverWidget-foreground, #ccc);
        box-shadow: 0 3px 10px rgba(0, 0, 0, 0.45);
        font-family: var(--vscode-font-family);
        font-size: var(--font-size-12);
        line-height: 1.4;
        /* A tooltip is never a target: it must not swallow the click that was heading for whatever
           it happens to be covering. */
        pointer-events: none;
    }

    /* Points back at the mark that raised it. A rotated square rather than a border triangle, so
       the two edges that show carry the bubble's own border. */
    .info-tip-arrow {
        position: absolute;
        width: 8px;
        height: 8px;
        margin-left: -4px;
        transform: rotate(45deg);
        background: var(--vscode-editorHoverWidget-background, #252526);
        border: var(--space-1) solid var(--vscode-editorHoverWidget-border, rgba(128, 128, 128, 0.35));
    }
    /* Only the two edges facing the mark are drawn; the rest would cut a line across the bubble. */
    .tip-above .info-tip-arrow { bottom: -5px; border-top: none; border-left: none; }
    .tip-below .info-tip-arrow { top: -5px; border-bottom: none; border-right: none; }

    /* A warning keeps the same bubble and takes a coloured edge. A wholly yellow panel of text is
       an alert; this is still a note, and it says which kind it is at a glance. */
    .info-tip.sev-warning { border-color: var(--vscode-editorWarning-foreground, #cca700); }
    .info-tip.sev-error { border-color: var(--vscode-editorError-foreground, #f14c4c); }
`;

export interface InfoBadgeProps {
    /** What the note is: an explanation, or something the reader should deal with. */
    severity?: BadgeSeverity;

    /** The note. Shown whole - the bubble is free to be as tall as it needs. */
    children: ReactNode;
}

export function InfoBadge({ severity = 'info', children }: InfoBadgeProps): React.JSX.Element {
    // Hover, and nothing else. Pressing used to PIN it open, which read as the tooltip refusing to
    // go away: the reader's first instinct on wanting rid of it is to move the pointer off, and
    // discovering that a second press is what closes it happens by accident if at all. A tooltip
    // that outlives the hover is not behaving like a tooltip.
    const [showing, setShowing] = useState(false);
    const [at, setAt] = useState<TooltipPlacement | null>(null);

    const mark = useRef<HTMLButtonElement | null>(null);
    const bubble = useRef<HTMLDivElement | null>(null);

    const open = showing;

    const place = useCallback(() => {
        if (mark.current === null || bubble.current === null) {
            return;
        }

        const anchor = mark.current.getBoundingClientRect();

        setAt(placeTooltip({
            anchor,
            size: { width: bubble.current.offsetWidth, height: bubble.current.offsetHeight },
            window: { width: window.innerWidth, height: window.innerHeight },
        }));
    }, []);

    // Before the browser paints: the bubble has to be rendered to be measured, and one painted
    // frame at the wrong place is a visible jump.
    useLayoutEffect(() => {
        if (open) {
            place();
        } else {
            setAt(null);
        }
    }, [open, place, children]);

    useLayoutEffect(() => {
        if (!open) {
            return;
        }

        const onKey = (event: KeyboardEvent): void => {
            if (event.key === 'Escape') {
                setShowing(false);
            }
        };

        // A tooltip pinned to a control that has since scrolled away points at nothing. Following
        // is cheaper than deciding when to give up, and `capture` catches the panel's own scroller
        // rather than only the window's.
        window.addEventListener('resize', place);
        window.addEventListener('scroll', place, true);
        document.addEventListener('keydown', onKey);

        return () => {
            window.removeEventListener('resize', place);
            window.removeEventListener('scroll', place, true);
            document.removeEventListener('keydown', onKey);
        };
    }, [open, place]);

    return (
        <>
            <button
                type="button"
                ref={mark}
                className={`info-badge sev-${severity}`}
                aria-expanded={open}
                aria-label="More about this"
                onPointerEnter={() => setShowing(true)}
                onPointerLeave={() => setShowing(false)}
                onFocus={() => setShowing(true)}
                onBlur={() => setShowing(false)}
                onClick={event => {
                    // Swallowed, not acted on. The mark sits inside labels and rows that have their
                    // own click behaviour, and pressing a tooltip's mark should do nothing at all.
                    event.stopPropagation();
                    event.preventDefault();
                }}
            >
                <Icon name={badgeIcon(severity)} size={14} />
            </button>

            {/* Into the body, not into the label. Every panel this appears in scrolls, and a bubble
                parented to a control inside one is clipped by it. */}
            {open && createPortal(
                <>
                <TooltipStyles />
                <div
                    ref={bubble}
                    role="tooltip"
                    className={`info-tip tip-${at?.side ?? 'above'} sev-${severity}`}
                    style={at === null
                        // Rendered to be measured, invisible until it has somewhere to be.
                        ? { visibility: 'hidden', left: 0, top: 0 }
                        : { left: at.left, top: at.top }}
                >
                    {children}
                    <span className="info-tip-arrow" style={{ left: at?.arrowLeft ?? 0 }} />
                </div>
                </>,
                document.body)}
        </>
    );
}
