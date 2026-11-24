// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { ReactNode } from "react";

import { DockTile, DockTileGrid } from "../shared/DockTile";
import { Icon, type IconName } from "../shared/Icon";
import { useReadOnly } from "./readOnly";

export interface LocTileProps {
    /** The tile's glyph, by meaning. */
    icon: IconName;
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

    /**
     * Whether the tile changes the file. Such a tile is disabled, saying why, for a file that is
     * shown and not edited; a tile that leaves the file alone - convert, export - stays as it is.
     */
    edits?: boolean;
}

/**
 * One tile in a {@link LocTileGrid}: the shared DockTile, its glyph named by meaning.
 *
 * Both docks are built from these: the file-level actions in either editor, and the credits step
 * library. They were two different shapes - a centred icon-over-label cell beside a full-width row
 * with a description - which read as two unrelated control sets stacked in the same dock.
 */
export function LocTile({
    icon,
    edits,
    ...tile
}: LocTileProps): React.JSX.Element {
    const readOnly = useReadOnly();
    const glyph = <Icon name={icon} />;
    // A tile that changes the file stays where it is for a file that is not edited, disabled and
    // saying why.
    return edits === true && readOnly !== null ? (
        <DockTile
            glyph={glyph}
            {...tile}
            draggable={false}
            disabled
            disabledReason={readOnly}
        />
    ) : (
        <DockTile glyph={glyph} {...tile} />
    );
}

/** The grid the tiles sit in - the shared DockTileGrid. */
export function LocTileGrid(props: { children: ReactNode }): React.JSX.Element {
    return <DockTileGrid>{props.children}</DockTileGrid>;
}
