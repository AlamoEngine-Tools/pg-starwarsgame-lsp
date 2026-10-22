// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One row of a model's tree - a bone, a mesh or an effect - as the model preview and the Mod
// Browser draw it: the twisty that folds its subtree, a picture of its kind, its name, and whatever
// controls the owner puts at its end (the preview's eye and details button). Its look is .tree-row
// in dockChromeCss; what a row is, and what clicking it does, is the owner's.

import { ReactNode } from "react";

import type { TreeKind } from "../preview/previewTree";
import { Icon, IconName } from "./Icon";

/** A kind's picture: a letter needed a legend to decode on every row of a hundred-row tree. */
export const TREE_KIND_ICONS: Record<TreeKind, IconName> = {
    bone: "skeleton",
    mesh: "mesh",
    particle: "effects",
};

/** What a kind is called - the row's tooltip and the filter chips. */
export const TREE_KIND_LABELS: Record<TreeKind, string> = {
    bone: "Bones",
    mesh: "Meshes",
    particle: "Effects",
};

/** How far one level of the tree indents its rows, and the indent of the top level. */
const INDENT = 10;
const INSET = 6;

export function TreeRow({
    id,
    kind,
    name,
    depth,
    expandable,
    expanded,
    onToggle,
    className,
    title,
    onClick,
    children,
}: {
    id: string;
    kind: TreeKind;
    name: string;
    depth: number;
    /** It has rows below it; a leaf keeps its twisty's place unseen, so the names line up. */
    expandable: boolean;
    expanded: boolean;
    onToggle: () => void;
    /** The owner's own classes on the row: its selection, its gating. */
    className?: string;
    /** The row's tooltip; its name by default. */
    title?: string;
    onClick?: (event: React.MouseEvent<HTMLLIElement>) => void;
    /** Controls at the row's end. */
    children?: ReactNode;
}): React.JSX.Element {
    return (
        <li
            className={className ? `tree-row ${className}` : "tree-row"}
            data-row={id}
            style={{ paddingLeft: INSET + depth * INDENT }}
            title={title ?? name}
            onClick={onClick}
        >
            <span
                className={expandable ? "twisty" : "twisty leaf"}
                onClick={(event) => {
                    event.stopPropagation();
                    onToggle();
                }}
            >
                <Icon name={expanded ? "expanded" : "collapsed"} size={13} />
            </span>
            <span
                className={`row-kind kind-${kind}`}
                title={TREE_KIND_LABELS[kind]}
            >
                <Icon name={TREE_KIND_ICONS[kind]} size={13} />
            </span>
            <span className="tree-name">{name}</span>
            {children}
        </li>
    );
}
