// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { ReactNode } from "react";

import { DockTile, DockTileGrid } from "../shared/DockTile";

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
 * One tile in a {@link LocTileGrid}: the shared DockTile with the codicon the loc docks still name.
 *
 * Both docks are built from these: the file-level actions in either editor, and the credits step
 * library. They were two different shapes - a centred icon-over-label cell beside a full-width row
 * with a description - which read as two unrelated control sets stacked in the same dock.
 */
export function LocTile({ icon, ...tile }: LocTileProps): React.JSX.Element {
    return (
        <DockTile
            glyph={<span className={`codicon codicon-${icon}`} />}
            {...tile}
        />
    );
}

/** The grid the tiles sit in - the shared DockTileGrid. */
export function LocTileGrid(props: { children: ReactNode }): React.JSX.Element {
    return <DockTileGrid>{props.children}</DockTileGrid>;
}
