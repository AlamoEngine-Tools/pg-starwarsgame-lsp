// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { ReactNode } from 'react';

export interface LocTileProps {
    /** Codicon name, without the `codicon-` prefix. */
    icon: string;
    label: string;
    /**
     * The raw token this tile stands for, shown under the label in the file's own monospace. Used
     * by the credits step library so the friendly name and the token in the Format column are
     * visibly the same thing.
     */
    badge?: string;
    title: string;
    onClick: () => void;

    draggable?: boolean;
    onDragStart?: (event: React.DragEvent) => void;
    onDragEnd?: () => void;
}

/**
 * One tile in a {@link LocTileGrid}.
 *
 * Both docks are built from these: the file-level actions in either editor, and the credits step
 * library. They were two different shapes - a centred icon-over-label cell beside a full-width row
 * with a description - which read as two unrelated control sets stacked in the same dock.
 */
export function LocTile(props: LocTileProps): React.JSX.Element {
    return (
        <button
            // A badge makes the tile three rows tall; the class is what keeps the label to one
            // line so all tiles stay the same height whatever they carry.
            className={`dock-tile${props.badge !== undefined ? ' with-badge' : ''}`}
            title={props.title}
            onClick={props.onClick}
            draggable={props.draggable}
            onDragStart={props.onDragStart}
            onDragEnd={props.onDragEnd}
        >
            <span className={`codicon codicon-${props.icon}`} />
            <span className="tile-label">{props.label}</span>
            {props.badge !== undefined && <span className="tile-badge">{props.badge}</span>}
        </button>
    );
}

/**
 * The grid the tiles sit in: as many columns as the dock has room for, two at its default width.
 * One rule for every tile group, so the docks cannot drift apart again.
 */
export function LocTileGrid(props: { children: ReactNode }): React.JSX.Element {
    return <div className="tile-grid">{props.children}</div>;
}
