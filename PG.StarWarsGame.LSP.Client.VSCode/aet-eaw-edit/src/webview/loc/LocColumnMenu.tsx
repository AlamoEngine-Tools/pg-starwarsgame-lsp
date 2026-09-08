// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { useEffect, useRef, useState } from 'react';
import { IconButton } from '../shared/Button';

export interface LocColumnMenuProps {
    languages: string[];
    hidden: Set<string>;
    onToggle: (language: string, visible: boolean) => void;
}

/**
 * The column picker: a gear at the right-hand end of the header row, opening a flyout.
 *
 * On the table rather than in the dock, because it is about this table's shape rather than about
 * the file - and because the dock is for things you do to the file, which this is not.
 */
export function LocColumnMenu(props: LocColumnMenuProps): React.JSX.Element {
    // Viewport coordinates, taken when the menu opens. The flyout is pinned to the viewport rather
    // than anchored to the gear: the header scrolls inside `.grid-area`, which clips anything reaching
    // past its edge - and the gear is at the far right, so an absolutely positioned flyout had its
    // labels cut off and showed a column of checkboxes with nothing beside them. Same reason the
    // row menu and the drop indicator are fixed.
    const [at, setAt] = useState<{ top: number; right: number } | null>(null);
    const ref = useRef<HTMLDivElement>(null);
    const buttonRef = useRef<HTMLButtonElement>(null);
    const open = at !== null;

    const toggle = (): void => {
        if (open) { setAt(null); return; }

        const box = buttonRef.current?.getBoundingClientRect();
        if (box) { setAt({ top: box.bottom + 2, right: window.innerWidth - box.right }); }
    };

    // Any click that is not inside the flyout closes it, which is what makes it feel like a menu
    // rather than a panel you have to put away.
    useEffect(() => {
        if (!open) { return; }

        const onDown = (e: MouseEvent): void => {
            if (!ref.current?.contains(e.target as Node)) { setAt(null); }
        };
        const onKey = (e: KeyboardEvent): void => {
            if (e.key === 'Escape') { setAt(null); }
        };
        // Pinned to the viewport, so it would otherwise sit still while the table moved under it.
        const onScrollOrResize = (): void => setAt(null);

        window.addEventListener('mousedown', onDown);
        window.addEventListener('keydown', onKey);
        window.addEventListener('resize', onScrollOrResize);
        document.querySelector('.grid-area')?.addEventListener('scroll', onScrollOrResize);
        return () => {
            window.removeEventListener('mousedown', onDown);
            window.removeEventListener('keydown', onKey);
            window.removeEventListener('resize', onScrollOrResize);
            document.querySelector('.grid-area')?.removeEventListener('scroll', onScrollOrResize);
        };
    }, [open]);

    const shown = props.languages.filter(l => !props.hidden.has(l));

    return (
        <div className="cell column-menu" ref={ref}>
            <IconButton
                ref={buttonRef}
                icon="settings"
                title="Choose which language columns to show"
                label="Columns"
                expanded={open}
                onClick={toggle}
            />

            {at !== null && (
                <div
                    className="column-flyout"
                    role="menu"
                    style={{ top: at.top, right: at.right }}
                >
                    <div className="column-flyout-title">
                        Columns
                        <span className="section-count">{shown.length} of {props.languages.length}</span>
                    </div>
                    {props.languages.map(language => (
                        <label key={language} className="check-field">
                            <input
                                type="checkbox"
                                checked={!props.hidden.has(language)}
                                // The last column standing cannot be hidden - a grid of keys with
                                // no values is not a view anyone asked for.
                                disabled={shown.length === 1 && !props.hidden.has(language)}
                                onChange={e => props.onToggle(language, e.target.checked)}
                            />
                            <span>{language}</span>
                        </label>
                    ))}
                </div>
            )}
        </div>
    );
}
