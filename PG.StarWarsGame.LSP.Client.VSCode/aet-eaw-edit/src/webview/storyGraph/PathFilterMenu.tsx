// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The event node's filter button, and the three directions behind it.
//
// One icon in the node header, as before - the header is where space is tightest and three buttons
// there would cost every node two more icons. Pressing it opens a small menu beside it: what leads
// here, the whole path, what follows from here.
//
// The menu is PORTALLED to the body and placed against the window, like the info tooltip, because a
// node lives inside rete's transformed content holder: a child of it would be scaled by the zoom and
// clipped by the canvas. `placeTooltip` decides above or below from the room available, so a node at
// the bottom edge opens upwards.

import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { createGlobalStyle } from 'styled-components';

import { tokensRootCss } from '../shared/tokens';
import { placeTooltip, type TooltipPlacement } from '../shared/tooltipPlace';

import { PATH_DIRECTIONS, type PathDirection } from './pathDirection';

const MenuStyles = createGlobalStyle`
    /* Portalled to the body, so it is inside no panel's Shell and inherits none of the tokens
       interpolated there - the same trap the info tooltip documents. */
    ${tokensRootCss}

    .path-menu {
        position: fixed;
        z-index: 40;
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
        padding: var(--space-4);
        border-radius: var(--radius-6);
        border: var(--space-1) solid var(--vscode-editorHoverWidget-border, rgba(128, 128, 128, 0.35));
        background: var(--vscode-editorHoverWidget-background, #252526);
        color: var(--vscode-editorHoverWidget-foreground, #ccc);
        box-shadow: 0 3px 10px rgba(0, 0, 0, 0.45);
        font-family: var(--vscode-font-family);
        font-size: var(--font-size-12);
    }

    /* Icon then label: the icon is what the reader learns to aim at, the label is what teaches it. */
    .path-menu button {
        display: flex;
        align-items: center;
        gap: var(--space-8);
        width: 100%;
        padding: var(--space-4) var(--space-8);
        border: none;
        border-radius: var(--radius-3);
        background: transparent;
        color: inherit;
        font: inherit;
        text-align: left;
        white-space: nowrap;
        cursor: pointer;
    }
    .path-menu button:hover { background: var(--vscode-toolbar-hoverBackground, rgba(128, 128, 128, 0.15)); }
    /* The direction already applied, so reopening the menu says where you are. */
    .path-menu button[aria-checked="true"] {
        background: var(--vscode-list-activeSelectionBackground, rgba(9, 71, 113, 0.6));
        color: var(--vscode-list-activeSelectionForeground, #fff);
    }
`;

export interface PathFilterMenuProps {
    /** Applies one direction. The caller supplies the event. */
    onPick: (direction: PathDirection) => void;

    /** The direction currently filtering on this event, if it is the filtered one. */
    active?: PathDirection;

    /** Opens the menu on first render - for tests, which cannot click. */
    initiallyOpen?: boolean;
}

export function PathFilterMenu({ onPick, active, initiallyOpen = false }: PathFilterMenuProps): React.JSX.Element {
    const [open, setOpen] = useState(initiallyOpen);
    const [placement, setPlacement] = useState<TooltipPlacement | null>(null);
    const buttonRef = useRef<HTMLButtonElement>(null);
    const menuRef = useRef<HTMLDivElement>(null);

    const place = useCallback(() => {
        const button = buttonRef.current;
        const menu = menuRef.current;
        if (!button || !menu) { return; }

        const anchor = button.getBoundingClientRect();
        const size = menu.getBoundingClientRect();
        setPlacement(placeTooltip({
            anchor,
            size: { width: size.width, height: size.height },
            window: { width: window.innerWidth, height: window.innerHeight },
        }));
    }, []);

    useLayoutEffect(() => {
        if (open) { place(); }
    }, [open, place]);

    // A menu that stays behind after the pointer has gone elsewhere is a menu in the way. Escape and
    // any press outside close it; so does scrolling or resizing, which would leave it detached from
    // the node it belongs to.
    useEffect(() => {
        if (!open) { return; }

        const close = (): void => setOpen(false);
        const onKey = (event: KeyboardEvent): void => { if (event.key === 'Escape') { close(); } };
        const onDown = (event: MouseEvent): void => {
            const target = event.target as Node;
            if (!menuRef.current?.contains(target) && !buttonRef.current?.contains(target)) { close(); }
        };

        window.addEventListener('keydown', onKey);
        window.addEventListener('mousedown', onDown, true);
        window.addEventListener('resize', close);
        window.addEventListener('wheel', close, true);
        return () => {
            window.removeEventListener('keydown', onKey);
            window.removeEventListener('mousedown', onDown, true);
            window.removeEventListener('resize', close);
            window.removeEventListener('wheel', close, true);
        };
    }, [open]);

    return (
        <>
            <button
                ref={buttonRef}
                title="Filter by path"
                aria-haspopup="menu"
                aria-expanded={open}
                onClick={() => setOpen(value => !value)}
            ><span className="codicon codicon-filter" /></button>
            {open && createPortal(
                <>
                    <MenuStyles />
                    <PathDirectionMenu
                        ref={menuRef}
                        active={active}
                        placement={placement}
                        onPick={direction => { setOpen(false); onPick(direction); }}
                    />
                </>,
                document.body)}
        </>
    );
}

export interface PathDirectionMenuProps {
    onPick: (direction: PathDirection) => void;
    active?: PathDirection;

    /** Where the menu sits, or null while it is being measured. */
    placement?: TooltipPlacement | null;
    ref?: React.Ref<HTMLDivElement>;
}

/**
 * The menu itself, without the portal - which is what makes it testable: rendering a portal needs a
 * document, and the webview tests render to a string.
 */
export function PathDirectionMenu(
    { onPick, active, placement = null, ref }: PathDirectionMenuProps,
): React.JSX.Element {
    return (
        <div
            ref={ref}
            className="path-menu"
            role="menu"
            aria-label="Filter by path"
            style={placement === null
                // Measured before it is placed: render it out of sight rather than at the corner,
                // where it would flash on every open.
                ? { visibility: 'hidden', left: 0, top: 0 }
                : { left: placement.left, top: placement.top }}
        >
            {PATH_DIRECTIONS.map(choice => (
                <button
                    key={choice.direction}
                    role="menuitemradio"
                    aria-checked={active === choice.direction}
                    onClick={() => onPick(choice.direction)}
                >
                    <span className={`codicon codicon-${choice.icon}`} />
                    {choice.label}
                </button>
            ))}
        </div>
    );
}
