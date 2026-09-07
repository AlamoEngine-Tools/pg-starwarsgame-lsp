// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A named, counted group of controls inside a dock.
//
// There were two of these. `.dock-section` was written out by hand in six files - a heading, a
// count, and no way to fold it. `.panel-section` was a component with the folding, used in one
// file, and disagreed with the other on gap, on body spacing and on which colour role the heading
// took. Both meant "a titled group in a dock". Nothing in the CSS said they were the same thing, so
// nothing stopped them drifting, and a token layer would have made both of them describable while
// leaving both of them shipped.
//
// One component now, folding when it is given something to fold with. The layout rules that were
// only ever prose are the tests beside this file: the count and any end content share ONE
// wrapper, because two auto margins split the free space and a chip appearing shoved the count
// eighty-eight pixels to the left; and a folded body is unmounted rather than hidden, because a
// folded section has no business holding a slider that still answers to the keyboard.

import { ReactNode } from 'react';

import { Icon } from './Icon';

export interface DockSectionProps {
    title: string;

    /** Shown beside the title where the number tells the reader something. */
    count?: number | string;

    /**
     * Chips or a button, packed against the far edge after the count.
     *
     * Goes inside the same wrapper the count does - see the note above about auto margins.
     */
    end?: ReactNode;

    /**
     * Stable id, used to remember whether this section is folded.
     *
     * Supplying this together with `onToggle` is what makes the section foldable. Without them it
     * is a heading, not a button that does nothing: a control that looks pressable and is not fails
     * the reader in the opposite direction from a hidden one.
     */
    id?: string;

    collapsed?: boolean;
    onToggle?: (id: string) => void;

    children: ReactNode;
}

export function DockSection({
    title, count, end, id, collapsed = false, onToggle, children,
}: DockSectionProps): React.JSX.Element {
    const foldable = id !== undefined && onToggle !== undefined;
    const folded = foldable && collapsed;

    const heading = (
        <>
            {foldable && <Icon name={folded ? 'collapsed' : 'expanded'} size={13} />}
            <span className="dock-section-name">{title}</span>

            {/* ONE auto margin, and it lives on this wrapper. */}
            {(count !== undefined || end !== undefined) && (
                <span className="title-end">
                    {count !== undefined && <span className="section-count">{count}</span>}
                    {end}
                </span>
            )}
        </>
    );

    return (
        <div className={`dock-section${folded ? ' collapsed' : ''}`}>
            {foldable ? (
                <button
                    type="button"
                    className="dock-section-title"
                    aria-expanded={!folded}
                    title={folded ? `Open ${title.toLowerCase()}` : `Fold ${title.toLowerCase()} away`}
                    onClick={() => onToggle(id)}
                >
                    {heading}
                </button>
            ) : (
                <div className="dock-section-title">{heading}</div>
            )}

            {!folded && <div className="dock-section-body">{children}</div>}
        </div>
    );
}
