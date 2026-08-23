// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One tree for everything the model is made of: bones, the meshes hanging off them, and the
// particle systems attached to them.
//
// Not three panels. A mesh's origin IS a bone and a particle system is attached to one, so the skeleton is
// already the structure they all live in - splitting them into a tree plus two lists made the reader
// reassemble that relationship in their head, and put mesh controls on whichever bone happened to
// own them (every mesh of a skinned model lands on the one bone they all share).
//
// Kept free of three.js so the whole shape - nesting, filtering, selection - is testable.

/** What a row stands for. The filter chips are exactly these. */
export type TreeKind = 'bone' | 'mesh' | 'particle';

/** One thing in the model, before nesting. */
export interface TreeItem {
    /**
     * Stable and unique across kinds - and stable across RELOADS, which the three.js uuid it used
     * to be was not.
     *
     * A merged bone/mesh row takes the BONE's id, because that is what its children already name
     * and what survives the file being opened again. Other meshes are keyed by the bone they hang
     * off and their position on it, never by name: sub-mesh names repeat inside a single model -
     * `Ai_rancor` has two called `Crusher#0` - so a name would make one row drive several.
     */
    id: string;
    kind: TreeKind;
    name: string;
    /** The id of the bone this hangs off, or null for a root. */
    parentId: string | null;
    visible: boolean;
    /** Hidden by the shader or the current damage/detail level rather than by hand. */
    gatedOff: boolean;

    /**
     * Which link of the visibility chain settled this row - see `visibility.ts`.
     *
     * Optional because a row can be built before the chain has run over it. Rendered as a sentence
     * on the row's tooltip: the tree could always say a row was hidden and never why, which is what
     * left a reader with a black scene and a list of ticks and nothing to go on.
     */
    because?: string;

    /**
     * The scene id of the effect this row carries, when it carries one.
     *
     * Carried rather than parsed back out of `id`. A proxy row is anchored on its BONE - the effect
     * and the bone are one thing (`effectPlacement`) - so `particle:<id>` is no longer a shape the
     * id can be assumed to have, and every consumer that sliced the prefix off was quietly reading
     * a bone index as a system. Deriving an identity from a formatted string is what this project
     * has now paid for three times; see `boneIds.ts`.
     */
    systemId?: string;
}

/**
 * The row id a bone gets, and the skeleton index it carries.
 *
 * One owner for the format, minted here and read here. It was minted in the viewport and read back
 * with a blind `slice(5)` in the panel - which is the shape `boneIds.ts` records this project
 * paying for three times, and it silently yields `NaN` the moment a mesh row reaches the same
 * handler.
 */
export function boneRowId(index: number): string {
    return `bone:${index}`;
}

/** The skeleton index in a bone row's id, or null for a row that is not a bone. */
export function boneIndexOfRow(id: string): number | null {
    const tail = id.startsWith('bone:') ? id.slice('bone:'.length) : '';

    return /^\d+$/.test(tail) ? Number.parseInt(tail, 10) : null;
}

/** A tree item with its children resolved. */
export interface TreeNode extends TreeItem {
    children: TreeNode[];
    depth: number;
}

/** Where a mesh belongs: folded into a bone's own row, or listed as a child of one. */
export type MeshPlacement = { merge: number } | { childOf: number };

/**
 * Decides whether a mesh IS its bone, or merely hangs off it.
 *
 * By NAME, because that is the model author's statement that the two are one thing: the ALO gives a
 * rigid sub-mesh the same name as the bone that is its origin. Collapsing those into one row is
 * what stops `Alttest.alo` drawing fourteen rows for its seven meshes.
 *
 * Deliberately NOT by which node they occupy. Sharing a node is the exporter's doing - it writes one
 * node per bone and puts the first mesh attached to that bone on the node itself - and merging on
 * that relabelled `Rv_nebulonb.alo`'s `Nebulon_parent`, the bone its engines and every hardpoint
 * descend from, as `COLLISION`. The loader now splits those apart before the tree ever sees them
 * (`boneNodes.ts`), so the question is only ever about names.
 *
 * A root never merges. It is the model's own origin rather than any mesh's, and it is where every
 * skinned mesh lands by default, so letting it merge handed the whole model's root row to whichever
 * mesh happened to be loaded first.
 */
export function meshPlacement(
    ownerBone: number, roots: ReadonlySet<number>, names: { bone: string; mesh: string },
): MeshPlacement {
    if (!roots.has(ownerBone) && names.bone.toLowerCase() === names.mesh.toLowerCase()) {
        return { merge: ownerBone };
    }

    return { childOf: ownerBone };
}

/**
 * Decides whether an effect IS its bone, or merely hangs off it.
 *
 * Same rule as a mesh's, and for the same reason: a particle PROXY is a bone named after the effect
 * it carries, so the name is the author saying the two are one thing. `p_atat_die` the bone and
 * `p_atat_die` the system are not a parent and a child - they are one row with one checkbox.
 *
 * It is not cosmetic. Separate rows put the effect BELOW its own bone in the visibility chain,
 * where a hidden ancestor is a veto that outranks the reader. The AT-AT's idle keys every effect
 * proxy off - correctly, since nothing is burning while it stands there - so every effect on the
 * model read `ancestor:p_atat_die` and no tick could bring one back. Merged, the clip's word
 * becomes a DECIDER on the row itself, which the reader's own word sits above.
 *
 * An effect on a bone with a different name - an engine wash on the engine block - stays a child,
 * because there the bone really is geometry in its own right.
 */
export function effectPlacement(
    ownerBone: number, roots: ReadonlySet<number>, names: { bone: string; effect: string },
): MeshPlacement {
    return meshPlacement(ownerBone, roots, { bone: names.bone, mesh: names.effect });
}

/**
 * Nests the items by `parentId`.
 *
 * An item whose parent is missing becomes a root rather than being dropped: it still exists and
 * still anchors something on screen, and losing it would hide geometry the viewport is drawing.
 * Bones come before their own meshes and effects at each level, so a bone's own geometry reads as
 * belonging to it rather than being lost among child bones.
 */
export function buildTree(items: readonly TreeItem[]): TreeNode[] {
    const nodes = new Map<string, TreeNode>();
    for (const item of items) {
        nodes.set(item.id, { ...item, children: [], depth: 0 });
    }

    const roots: TreeNode[] = [];

    for (const item of items) {
        const node = nodes.get(item.id)!;
        const parent = item.parentId === null ? undefined : nodes.get(item.parentId);

        if (parent === undefined || parent === node) {
            roots.push(node);
        } else {
            parent.children.push(node);
        }
    }

    const order: Record<TreeKind, number> = { bone: 0, mesh: 1, particle: 2 };
    const sortChildren = (node: TreeNode, depth: number): void => {
        node.depth = depth;
        node.children.sort((a, b) => order[a.kind] - order[b.kind]);

        for (const child of node.children) {
            sortChildren(child, depth + 1);
        }
    };

    for (const root of roots) {
        sortChildren(root, 0);
    }

    return roots;
}

/**
 * The subtrees to start collapsed: the ones that lead to no geometry and no effect.
 *
 * Most of a skeleton is scaffolding the reader did not open the tree to read - 50 of the Star
 * Destroyer's 74 rows are bones carrying nothing, and 45 of the rancor's 58 - so opening with all
 * of it expanded buries the handful of rows that were being looked for. Collapsed rather than
 * hidden: the structure is still there, one click away, and a bone that anchors a hardpoint is
 * exactly what someone will eventually want.
 *
 * Only the TOPMOST empty limb is named. Collapsing its children too would be redundant, and would
 * leave the reader clicking through several layers of already-hidden rows.
 */
export function defaultCollapsed(roots: readonly TreeNode[]): Set<string> {
    const collapsed = new Set<string>();

    const carries = (node: TreeNode): boolean =>
        node.kind !== 'bone' || node.children.some(carries);

    const walk = (node: TreeNode, depth: number): void => {
        // Never the root itself, which would hide the model behind one twisty.
        if (depth > 0 && node.children.length > 0 && !carries(node)) {
            collapsed.add(node.id);
            return;
        }

        for (const child of node.children) {
            walk(child, depth + 1);
        }
    };

    for (const root of roots) {
        walk(root, 0);
    }

    return collapsed;
}

/** What the filter row asks for. */
export interface TreeFilter {
    text: string;
    /** Kinds to show. An empty set shows nothing, which is what unticking everything should do. */
    kinds: ReadonlySet<TreeKind>;
}

/**
 * Narrows the tree to what the filter asks for, keeping every ancestor of a match.
 *
 * Ancestors are kept so a match stays reachable, but they are NOT themselves treated as matches -
 * a bone retained only to hold a matching mesh does not then drag in its other children. That is
 * what makes filtering to `particle` show the handful of bones that carry one rather than the whole
 * skeleton.
 */
export function filterTree(roots: readonly TreeNode[], filter: TreeFilter): TreeNode[] {
    const needle = filter.text.trim().toLowerCase();

    const keep = (node: TreeNode): TreeNode | null => {
        const children = node.children
            .map(keep)
            .filter((child): child is TreeNode => child !== null);

        const matches = filter.kinds.has(node.kind)
            && (needle === '' || node.name.toLowerCase().includes(needle));

        if (!matches && children.length === 0) {
            return null;
        }

        // A match keeps its own children; an ancestor kept only for a descendant shows just the
        // path down to it.
        return { ...node, children: matches ? node.children : children };
    };

    return roots.map(keep).filter((node): node is TreeNode => node !== null);
}

/** A row as drawn, with the twisty state the caller needs. */
export interface TreeRow {
    node: TreeNode;
    expandable: boolean;
    expanded: boolean;
}

/**
 * Flattens the tree into the rows to draw.
 *
 * Collapsed by exception rather than by default: a skeleton is usually shallow, and hiding it behind
 * twisties on open makes the tree useless at a glance.
 */
export function visibleTreeRows(
    roots: readonly TreeNode[], collapsed: ReadonlySet<string>,
): TreeRow[] {
    const rows: TreeRow[] = [];

    const walk = (node: TreeNode): void => {
        const expanded = !collapsed.has(node.id);
        rows.push({ node, expandable: node.children.length > 0, expanded });

        if (expanded) {
            for (const child of node.children) {
                walk(child);
            }
        }
    };

    for (const root of roots) {
        walk(root);
    }

    return rows;
}

/**
 * The ids a click selects, given what was already selected.
 *
 * Plain click replaces, ctrl/cmd toggles one, shift takes the run between the last anchor and here -
 * over the ROWS AS DRAWN, so a shift-select follows what the reader can see rather than some
 * underlying order they have no view of.
 */
export function selectionAfterClick(
    rows: readonly TreeRow[],
    selected: ReadonlySet<string>,
    anchor: string | null,
    clicked: string,
    modifiers: { ctrl: boolean; shift: boolean },
): { selected: Set<string>; anchor: string } {
    if (modifiers.shift && anchor !== null) {
        const from = rows.findIndex(row => row.node.id === anchor);
        const to = rows.findIndex(row => row.node.id === clicked);

        if (from !== -1 && to !== -1) {
            const run = rows.slice(Math.min(from, to), Math.max(from, to) + 1);

            // Extends rather than replaces, so ctrl-picking a few then shift-extending works.
            return {
                selected: new Set([...selected, ...run.map(row => row.node.id)]),
                anchor,
            };
        }
    }

    if (modifiers.ctrl) {
        const next = new Set(selected);
        if (!next.delete(clicked)) {
            next.add(clicked);
        }

        return { selected: next, anchor: clicked };
    }

    // The last one standing lets go. A selection draws a box in the viewport, so there has to be a
    // way to stop drawing one - and the row you want to let go of is the row under the pointer.
    // Ctrl-click already toggled, but that is not a thing anyone discovers.
    //
    // Only when it is the ONLY one selected: with several picked, a plain click means "just this
    // one", which is what every list does and what a reader narrowing a multi-select expects.
    if (selected.size === 1 && selected.has(clicked)) {
        return { selected: new Set(), anchor: clicked };
    }

    return { selected: new Set([clicked]), anchor: clicked };
}

/**
 * The ids one visibility checkbox should apply to.
 *
 * Ticking a row inside a selection moves the WHOLE selection - that is the point of selecting - but
 * ticking one outside it moves only that row, rather than silently acting on things elsewhere in a
 * long tree that the reader may have forgotten were selected.
 */
export function toggleTargets(selected: ReadonlySet<string>, clicked: string): string[] {
    return selected.has(clicked) ? [...selected] : [clicked];
}

/**
 * Every id in the subtrees rooted at `ids`, the roots included.
 *
 * A tree row that hides itself and leaves its children on screen is not behaving like a tree.
 * Hiding a limb has to take the limb with it - which for a skeleton is the whole point, since the
 * geometry hanging off a bone is what the reader wants gone.
 */
export function withDescendants(
    roots: readonly TreeNode[], ids: readonly string[],
): string[] {
    const wanted = new Set(ids);
    const out = new Set<string>();

    const collect = (node: TreeNode): void => {
        out.add(node.id);
        for (const child of node.children) {
            collect(child);
        }
    };

    const walk = (node: TreeNode): void => {
        if (wanted.has(node.id)) {
            collect(node);
            return;
        }

        for (const child of node.children) {
            walk(child);
        }
    };

    for (const root of roots) {
        walk(root);
    }

    // Anything asked for that is not in the tree right now - filtered out, say - still counts.
    for (const id of ids) {
        out.add(id);
    }

    return [...out];
}
