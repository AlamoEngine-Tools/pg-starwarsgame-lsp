// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The skeleton tree: shape, filtering and label policy.
//
// Pure on purpose. The viewport owns the geometry - where a joint lands on screen - and this owns
// every decision about it, so the decisions can be tested. "The tree hid the bone I searched for"
// is otherwise only ever found by looking at it.

/** One bone, as read back off the loaded scene. */
export interface FlatBone {
    /** Position in the model's bone list, which is what the XML and the animation tracks index by. */
    index: number;
    /** The Alamo name, with the exporter's uniqueness suffix already stripped. */
    name: string;
    /** Index of the parent bone, or -1 for a root. */
    parent: number;
    /** Whether the model marks the bone visible. Hidden bones still exist and still anchor things. */
    visible: boolean;
}

/** Something attached to a bone. */
export interface BoneAttachment {
    kind: 'mesh' | 'hardpoint';
    label: string;
    /**
     * The mesh's own identity, for the visibility toggle. Meshes only.
     *
     * The three.js uuid rather than the name: an assembled unit carries the same turret model eight
     * times, so `<MeshName>#<index>` is shared between eight different meshes and a name-keyed
     * toggle switches all of them at once.
     */
    meshId?: string;
    /** Whether it is currently drawn, so the tree can show a checked box. */
    visible?: boolean;
}

/** A bone with its children resolved. */
export interface BoneNode extends FlatBone {
    children: BoneNode[];
    depth: number;
    attachments: BoneAttachment[];
}

/** How many bone labels the viewport draws. */
export type LabelMode = 'none' | 'selected' | 'all';

/**
 * The exporter appends `#index` to every glTF node name, because shipped skeletons repeat names - a
 * Star Destroyer hull carries twenty bones called `p_hp_imperial_damage`. This recovers the name the
 * XML actually references.
 */
export function alamoBoneName(nodeName: string): { name: string; index: number } | null {
    const hash = nodeName.lastIndexOf('#');
    if (hash <= 0) {
        return null;
    }

    const index = Number.parseInt(nodeName.slice(hash + 1), 10);
    if (!Number.isInteger(index) || index < 0) {
        return null;
    }

    return { name: nodeName.slice(0, hash), index };
}

/**
 * Builds the forest.
 *
 * A parent index outside the list, or one pointing forward, is treated as a root rather than
 * dropped: the bone still exists and still anchors whatever is attached to it, and losing it from the
 * tree would hide geometry that is plainly on screen.
 */
export function buildBoneTree(
    bones: readonly FlatBone[],
    attachments: ReadonlyMap<number, BoneAttachment[]> = new Map(),
): BoneNode[] {
    const nodes = bones.map<BoneNode>(bone => ({
        ...bone,
        children: [],
        depth: 0,
        attachments: attachments.get(bone.index) ?? [],
    }));

    const roots: BoneNode[] = [];

    for (const node of nodes) {
        const parent = node.parent >= 0 && node.parent < nodes.length && node.parent !== node.index
            ? nodes[node.parent]
            : null;

        if (parent === null) {
            roots.push(node);
        } else {
            parent.children.push(node);
        }
    }

    for (const root of roots) {
        setDepth(root, 0);
    }

    return roots;
}

function setDepth(node: BoneNode, depth: number): void {
    node.depth = depth;
    for (const child of node.children) {
        setDepth(child, depth + 1);
    }
}

/**
 * Narrows the tree to bones matching <paramref name="query" />, keeping each match's ancestors.
 *
 * Ancestors are kept so a match stays reachable and its place in the hierarchy is still readable -
 * a flat list of matches would answer "which bones" but not "where". Descendants are dropped: typing
 * `HP_` should give the hardpoint bones, not also the twenty damage proxies attached to them.
 */
export function filterBones(roots: readonly BoneNode[], query: string): BoneNode[] {
    const needle = query.trim().toLowerCase();
    if (needle === '') {
        return [...roots];
    }

    const keep = (node: BoneNode): BoneNode | null => {
        const matches = node.name.toLowerCase().includes(needle);
        const children = node.children
            .map(keep)
            .filter((child): child is BoneNode => child !== null);

        if (!matches && children.length === 0) {
            return null;
        }

        // A match keeps its own subtree collapsed away; an ancestor keeps only the path down.
        return { ...node, children: matches ? [] : children };
    };

    return roots.map(keep).filter((node): node is BoneNode => node !== null);
}

/** A row of the rendered tree. */
export interface BoneRow {
    node: BoneNode;
    /** Whether this row has children to reveal, so the caller knows to draw a twisty. */
    expandable: boolean;
    expanded: boolean;
}

/**
 * The rows to draw, honouring which nodes are collapsed.
 *
 * Collapsed by exception rather than by default: a skeleton is usually shallow and hiding it behind
 * twisties on open makes the tree useless at a glance.
 */
export function visibleRows(
    roots: readonly BoneNode[], collapsed: ReadonlySet<number>,
): BoneRow[] {
    const rows: BoneRow[] = [];

    const walk = (node: BoneNode): void => {
        const expanded = !collapsed.has(node.index);
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
 * Which bones want a label in the viewport.
 *
 * Only the policy: the viewport still drops labels that would overlap on screen, which needs
 * projected positions and cannot be decided here.
 */
export function labelCandidates(
    bones: readonly FlatBone[], mode: LabelMode, selected: ReadonlySet<number>,
): Set<number> {
    if (mode === 'none') {
        return new Set();
    }

    // A SET, because the panel selects several at once - two fire bones, a shift-run in the tree.
    // It took one index, so a selection of three was labelled once and the other two read as
    // unselected.
    if (mode === 'selected') {
        return new Set(selected);
    }

    return new Set(bones.map(bone => bone.index));
}

/** The chain from a root down to <paramref name="index" />, for revealing a selection. */
export function ancestorsOf(bones: readonly FlatBone[], index: number): number[] {
    const byIndex = new Map(bones.map(bone => [bone.index, bone]));
    const chain: number[] = [];

    let current = byIndex.get(index)?.parent ?? -1;
    // Bounded by the bone count: a malformed file could otherwise describe a parent cycle and hang
    // the panel rather than the reader, which already rejects such files.
    while (current >= 0 && chain.length <= bones.length) {
        chain.unshift(current);
        current = byIndex.get(current)?.parent ?? -1;
    }

    return chain;
}
