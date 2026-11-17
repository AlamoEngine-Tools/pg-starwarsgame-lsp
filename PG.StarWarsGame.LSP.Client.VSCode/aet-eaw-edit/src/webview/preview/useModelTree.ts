// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Model tree: its rows, what is folded, the search and the kinds it is filtered by, the header
// summary of what the filter hides, and the row and Reset actions. One hook so the Model tree section
// reads one object.

import {
    useCallback,
    useMemo,
    useRef,
    useState,
    type Dispatch,
    type SetStateAction,
    type RefObject,
} from "react";
import { TREE_KINDS } from "./previewLabels";
import {
    buildTree,
    filterTree,
    toggleTargets,
    type TreeItem,
    type TreeKind,
    visibleTreeRows,
    withDescendants,
} from "./previewTree";
import { type PreviewViewport } from "./viewport";
import { setRow } from "./visibility";
import { type DefinedLevels } from "./levels";
import { countTreeNodes, searchIsOpen, treeFilterSummary } from "./treeSearch";
import { modelTouched } from "./previewMode";

/** What useModelTree reads from the rest of the preview. */
export interface ModelTreeInputs {
    alt: number;
    hiddenEmitters: ReadonlySet<number>;
    levels: DefinedLevels;
    lod: number;
    searchPressed: boolean;
    selected: ReadonlySet<string>;
    setAlt: Dispatch<SetStateAction<number>>;
    setHiddenEmitters: Dispatch<SetStateAction<ReadonlySet<number>>>;
    setLod: Dispatch<SetStateAction<number>>;
    setTreeItems: Dispatch<SetStateAction<TreeItem[]>>;
    treeItems: TreeItem[];
    viewportRef: RefObject<PreviewViewport | null>;
}

export type ModelTree = ReturnType<typeof useModelTree>;

export function useModelTree({
    alt,
    hiddenEmitters,
    levels,
    lod,
    searchPressed,
    selected,
    setAlt,
    setHiddenEmitters,
    setLod,
    setTreeItems,
    treeItems,
    viewportRef,
}: ModelTreeInputs) {
    const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set());

    /** So the opening fold happens once per subject rather than every time the tree is rebuilt. */
    const foldedRef = useRef(false);
    const [boneFilter, setBoneFilter] = useState("");

    /** Which kinds the tree shows. All three to begin with. */
    const [kinds, setKinds] = useState<ReadonlySet<TreeKind>>(
        new Set<TreeKind>(TREE_KINDS),
    );

    /**
     * The systems playing right now, by scene id.
     *
     * Taken from the tree items, which the viewport rebuilds after every change - so the group
     * switches and the tree rows are reading the same snapshot and cannot drift apart.
     */
    const shownSystems = useMemo(
        () =>
            new Set(
                treeItems
                    .filter(
                        (item) => item.visible && item.systemId !== undefined,
                    )
                    .map((item) => item.systemId!),
            ),
        [treeItems],
    );

    const wholeTree = buildTree(treeItems);
    const filteredTree = filterTree(wholeTree, { text: boneFilter, kinds });
    const treeRows = visibleTreeRows(filteredTree, collapsed);

    /* What the tree would show with no filter at all, which is the height it is held at. Folding a
       branch still changes it - that is the reader asking for less tree - but filtering cannot. */
    const unfilteredRowCount = visibleTreeRows(wholeTree, collapsed).length;

    /* Showing, which is not the same as pressed: a pattern keeps its own box open. */
    const searchOpen = searchIsOpen(searchPressed, boneFilter);

    /* What the header says when the tree is showing less than the model has. The count alone
       cannot carry it - "40" over a Star Destroyer reads as a small model, and once the search
       element is collapsed neither the pattern nor a switched-off kind is on screen at all.

       Measured on the FILTERED tree rather than on `treeRows`, which has already had every
       collapsed branch dropped from it. Folding is not filtering, and counting the rendered rows
       had a fresh model announcing itself as narrowed the moment it opened. */
    const filterSummary = treeFilterSummary(
        countTreeNodes(filteredTree),
        treeItems.length,
        kinds.size,
    );

    // Deliberately no longer cleared when the row scrolls out of the tree.
    //
    // The flyout had to be: it was pinned to the row, so filtering the row away or collapsing its
    // parent left a panel hanging beside nothing. A tab is not pinned to anything, and dropping the
    // row on a filter keystroke would mean typing in the tree search silently stopped the inspector
    // tracking what it is showing. The row is still in `treeItems` either way, so the facts stay
    // right; only its visibility in this list changed, which is not a fact about the row.

    /**
     * Whether the model has been moved off the state it opened in.
     *
     * Reads the same three things Reset puts back, so the button cannot claim there is nothing to
     * do while something plainly is - and cannot offer to reset a model already at rest.
     */
    const touched = modelTouched({
        alt,
        lodIsHighest:
            levels.lod.length === 0 ||
            lod === levels.lod[levels.lod.length - 1],
        hiddenEmitters: hiddenEmitters.size,
        rowOverrides: viewportRef.current?.rowOverrideEntries().length ?? 0,
    });

    /**
     * Puts the model back the way it opened.
     *
     * The OPENING RULES, which are a decision of their own: undamaged, at the highest detail the
     * model defines, and quiet. Not "the defaults" - the highest LOD is the LAST defined one,
     * because Alamo numbers detail the other way up.
     *
     * The room is left alone. The grid, the lights and the backdrop are not part of the model, and
     * someone resetting a hull they have been unpicking for ten minutes has not asked for their
     * lighting back.
     *
     * The GAMEPLAY lens is left alone too, and for the stronger reason. This button sits in the
     * Model tree's header and puts the MODEL back; a destroyed hardpoint and a hidden firing arc
     * belong to the lens that can undo them. Clearing `hiddenWeapons` from here revealed every
     * firing arc on the hull the moment it was pressed - in a lens that draws no arcs and offers no
     * pill to put them away. See `modelTouched`, which is the same boundary drawn once.
     */
    const resetModel = useCallback(() => {
        viewportRef.current?.clearRowOverrides();
        setAlt(0);
        setLod((current) =>
            levels.lod.length > 0 ? levels.lod[levels.lod.length - 1] : current,
        );
        setHiddenEmitters(new Set());
        setTreeItems(viewportRef.current?.treeItems() ?? []);
    }, [levels]);

    /**
     * Applies a visibility change to the clicked row, or to the whole selection it belongs to -
     * and in either case to everything beneath, the way a tree is expected to behave.
     */
    const setRowsVisible = (id: string, visible: boolean): void => {
        const roots = buildTree(treeItems);
        const clicked = toggleTargets(selected, id);

        // `visible` is what the reader is asking FOR - a checkbox's change event carries the
        // state it is moving to, and a button labelled Hide says what it will do. It used to be
        // handed to `toggleRow`, which is told what the reader can SEE and flips it, so every one
        // of these was inverted: the tick set each row to the state it was already in and looked
        // dead, and Hide showed things while Show hid them.
        //
        // Hiding pushes down the subtree; showing clears what is under them, so switching a bone
        // back on does not drag its collision hull into view.
        //
        // Each row's own `authored` goes in - `gatedOff` IS `!authored`, straight off the chain -
        // because `setRow` stores the reader's word only where it disagrees with the model. Per row
        // and not once for the click: a multi-select spans meshes the model draws and shadow
        // volumes it does not, and one answer for the lot would be wrong for half of them.
        const authoredOf = new Map(
            treeItems.map((item) => [item.id, !item.gatedOff]),
        );

        const changes = clicked.flatMap((row) =>
            setRow(
                row,
                withDescendants(roots, [row]).filter((under) => under !== row),
                visible,
                authoredOf.get(row) ?? true,
            ),
        );

        viewportRef.current?.applyRowOverrides(changes);
        setTreeItems(viewportRef.current?.treeItems() ?? []);
    };

    const toggleCollapsed = (index: string): void =>
        setCollapsed((current) => {
            const next = new Set(current);
            if (!next.delete(index)) {
                next.add(index);
            }
            return next;
        });

    return {
        collapsed,
        setCollapsed,
        foldedRef,
        boneFilter,
        setBoneFilter,
        kinds,
        setKinds,
        shownSystems,
        treeRows,
        unfilteredRowCount,
        searchOpen,
        filterSummary,
        touched,
        resetModel,
        setRowsVisible,
        toggleCollapsed,
    };
}
