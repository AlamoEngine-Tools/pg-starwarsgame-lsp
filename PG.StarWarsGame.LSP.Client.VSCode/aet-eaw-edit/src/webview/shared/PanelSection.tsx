// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A named, foldable group of controls inside a panel.
//
// Laid out after BSI CX's step library, which is this extension's reference for a dock full of
// things: a small coloured heading, a count where the number means something, a chevron that folds
// the group away, and enough space between groups that the eye lands on a heading rather than on a
// wall of controls. A flat list of fourteen settings has no shape - the reader has to read all of
// it to find the one they came for.
//
// The heading is the whole hit area, not just the chevron. A 12px target next to a word that
// obviously names the thing being folded is a target people miss.

import { ReactNode } from 'react';

export interface PanelSectionProps {
    /** Stable id, used to remember whether this section is folded. */
    id: string;

    title: string;

    /** Shown beside the title where the number tells the reader something. */
    count?: number | string;

    collapsed: boolean;
    onToggle: (id: string) => void;

    children: ReactNode;
}

export function PanelSection({
    id, title, count, collapsed, onToggle, children,
}: PanelSectionProps): React.JSX.Element {
    return (
        <div className={`panel-section${collapsed ? ' collapsed' : ''}`}>
            <button
                type="button"
                className="panel-section-head"
                aria-expanded={!collapsed}
                title={collapsed ? `Open ${title.toLowerCase()}` : `Fold ${title.toLowerCase()} away`}
                onClick={() => onToggle(id)}
            >
                <span
                    className={`codicon codicon-chevron-${collapsed ? 'right' : 'down'}`}
                    aria-hidden="true"
                />
                <span className="panel-section-title">{title}</span>
                {count !== undefined && <span className="panel-section-count">{count}</span>}
            </button>

            {/* Unmounted rather than hidden: these are live controls bound to the viewport, and a
                folded section has no business holding a slider that still answers to the keyboard. */}
            {!collapsed && <div className="panel-section-body">{children}</div>}
        </div>
    );
}
