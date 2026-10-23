// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One ACTION tile, the shape every dock's actions take: the loc editors' file actions and step
// library, the story graph's palette, a Mod Browser entry's actions. Its look is .dock-tile and the
// grid it sits in .tile-grid, both in dockChromeCss.

import { ReactNode } from "react";

import type { Enablement } from "./Button";

export type DockTileProps = Enablement & {
    /** The tile's icon: a Tabler Icon, or a codicon span where a loc dock still uses one. */
    glyph: ReactNode;
    label: string;
    /**
     * Under the label: the raw token a credits step stands for, in the file's own monospace. A
     * disabled tile shows its reason here instead, since a tooltip is not where a reader looks.
     */
    badge?: string;
    title: string;
    onClick: () => void;

    draggable?: boolean;
    onDragStart?: (event: React.DragEvent) => void;
    onDragEnd?: () => void;
};

export function DockTile({
    glyph,
    label,
    badge,
    title,
    onClick,
    disabled,
    disabledReason,
    draggable,
    onDragStart,
    onDragEnd,
}: DockTileProps): React.JSX.Element {
    const shown = disabled ? disabledReason : badge;
    return (
        <button
            type="button"
            // A badge makes the tile three rows tall; the class is what keeps the label to one
            // line so all tiles stay the same height whatever they carry.
            className={`dock-tile${shown !== undefined ? " with-badge" : ""}`}
            title={disabled ? disabledReason : title}
            disabled={disabled}
            onClick={onClick}
            draggable={draggable}
            onDragStart={onDragStart}
            onDragEnd={onDragEnd}
        >
            <span className="tile-glyph">{glyph}</span>
            <span className="tile-label">{label}</span>
            {shown !== undefined && <span className="tile-badge">{shown}</span>}
        </button>
    );
}

/**
 * The grid the tiles sit in: as many columns as the dock has room for. One rule for every tile
 * group, so the docks cannot drift apart again.
 */
export function DockTileGrid({
    children,
}: {
    children: ReactNode;
}): React.JSX.Element {
    return <div className="tile-grid">{children}</div>;
}
