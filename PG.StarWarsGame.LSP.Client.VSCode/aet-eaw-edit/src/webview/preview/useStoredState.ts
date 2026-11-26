// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the preview remembers: the tree folded once the load is whole, the attacker written to
// the project, and where this subject was left written for the session.

import {
    useEffect,
    type Dispatch,
    type SetStateAction,
    type RefObject,
} from "react";
import { post } from "./previewPost";
import { buildTree, defaultCollapsed, type TreeItem } from "./previewTree";
import { type PreviewViewport } from "./viewport";
import { lodToRecord, type SubjectState } from "../../shared/subjectState";
import { type SearchMode } from "../shared/searchPattern";
import { type LoadState } from "./loadProgress";
import {
    type Attacker,
    type AttackerPreset,
} from "../../shared/projectSettings";

/** What useStoredState reads from the rest of the preview. */
export interface StoredStateInputs {
    alt: number;
    animation: string | null;
    attacker: Attacker;
    attackerPresets: AttackerPreset[];
    boneFilter: string;
    boneFilterMode: SearchMode;
    collapsed: ReadonlySet<string>;
    customColour: string | null;
    destroyed: ReadonlySet<string>;
    faction: string;
    foldedRef: RefObject<boolean>;
    hiddenEmitters: ReadonlySet<number>;
    loading: LoadState;
    lod: number;
    /** Whether the reader picked `lod`, rather than the opening rule following the model. */
    lodPickedRef: RefObject<boolean>;
    restoredRef: RefObject<boolean>;
    setCollapsed: Dispatch<SetStateAction<ReadonlySet<string>>>;
    setTreeItems: Dispatch<SetStateAction<TreeItem[]>>;
    storedSubjectRef: RefObject<SubjectState | null>;
    treeItems: TreeItem[];
    viewportRef: RefObject<PreviewViewport | null>;
}

export function useStoredState({
    alt,
    animation,
    attacker,
    attackerPresets,
    boneFilter,
    boneFilterMode,
    collapsed,
    customColour,
    destroyed,
    faction,
    foldedRef,
    hiddenEmitters,
    loading,
    lod,
    lodPickedRef,
    restoredRef,
    setCollapsed,
    setTreeItems,
    storedSubjectRef,
    treeItems,
    viewportRef,
}: StoredStateInputs) {
    /**
     * Folds the scaffolding away the first time a subject's rows arrive.
     *
     * Most of a skeleton is structure nobody opened the tree to read - 50 of the Star Destroyer's
     * 74 rows are bones carrying no geometry and no effect - and with all of it expanded the few
     * rows being looked for are buried. Collapsed, not hidden: one click brings any limb back.
     *
     * Once per subject, so a rebuild after a level change or a toggle cannot re-fold something the
     * reader has just opened.
     *
     * Not on the first rows to ARRIVE, though - on the first rows once the subject has finished
     * assembling. Parts and their effects land one at a time, so the first non-empty tree is a bare
     * skeleton whose damage bones carry nothing yet, and `defaultCollapsed` folds a limb precisely
     * because it carries nothing. Every effect that had not landed was then buried: a Star
     * Destroyer opened with 20 of its 22 proxies behind twisties, and filtering the tree to effects
     * showed 2 of them. The load cover coming off is the signal that the tree is the whole tree.
     */
    useEffect(() => {
        if (foldedRef.current || loading.covered || treeItems.length === 0) {
            return;
        }

        foldedRef.current = true;
        setCollapsed((current) =>
            current.size > 0 ? current : defaultCollapsed(buildTree(treeItems)),
        );

        // And whatever the reader had switched by hand, now that a mesh key survives a reload.
        // Both directions: a hidden mesh and a system deliberately lit are equally deliberate, and
        // a system is usually off to begin with so only `shown` can carry it back.
        const hidden = storedSubjectRef.current?.hidden ?? [];
        const shown = storedSubjectRef.current?.shown ?? [];

        if (hidden.length > 0 || shown.length > 0) {
            viewportRef.current?.setItemsVisible(hidden, false);
            viewportRef.current?.setItemsVisible(shown, true);
            setTreeItems(viewportRef.current?.treeItems() ?? []);
        }
    }, [treeItems, loading.covered]);

    /**
     * Records what belongs to the PROJECT rather than to the person.
     *
     * Its own message and its own effect for that reason: the room goes to `globalState` and this
     * goes to `workspaceState`, so writing them together would put one of them at the wrong tier.
     * Every field here names something out of the mod's own tree - a damage type, a faction - and a
     * damage type typed against one mod arriving in the next is the fault that moved them.
     */
    useEffect(() => {
        // The same guard the room's own write uses: before the stored bench has been applied the
        // first frame would overwrite it with defaults.
        if (!restoredRef.current) {
            return;
        }

        post({
            type: "setProjectSettings",
            project: {
                attacker,
                presets: attackerPresets,
                faction: faction === "" ? null : faction,
                customColour,
            },
        });
    }, [attacker, attackerPresets, faction, customColour]);

    /**
     * Remembers where this subject was left, for as long as the window lives.
     *
     * Session-scoped on purpose: reopening the same model should pick up where you were, while a
     * fresh window must obey the opening rules rather than restore last week's damage.
     *
     * The detail level only once the reader picked it. Until then the opening rule follows the
     * geometry as it arrives, from a placeholder LOD 0, and recording that made a preview closed
     * during the load reopen at LOD 0 - the lowest detail in Alamo. See `lodToRecord`.
     */
    useEffect(() => {
        if (!restoredRef.current) {
            return;
        }

        post({
            type: "setSubjectState",
            state: {
                alt,
                lod: lodToRecord(
                    lod,
                    lodPickedRef.current,
                    storedSubjectRef.current?.lod ?? null,
                ),
                destroyed: [...destroyed],
                hiddenEmitters: [...hiddenEmitters],
                animation,
                filterText: boneFilter,
                filterMode: boneFilterMode,
                collapsed: [...collapsed],
                hidden: viewportRef.current?.hiddenItemIds() ?? [],
                shown: viewportRef.current?.shownItemIds() ?? [],
            } satisfies SubjectState,
        });
    }, [
        alt,
        lod,
        destroyed,
        hiddenEmitters,
        animation,
        boneFilter,
        boneFilterMode,
        collapsed,
        treeItems,
    ]);

    return {};
}
