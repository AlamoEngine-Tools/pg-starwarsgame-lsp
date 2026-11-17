// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The inspector tab: what the open row is made of, sent to the tab whenever it changes, and the
// call that opens the tab on a row.

import {
    useCallback,
    useEffect,
    useMemo,
    type Dispatch,
    type SetStateAction,
    type RefObject,
} from "react";
import { type ModelDetail } from "../../protocol/modelPreview";
import { type InspectorSubject } from "./inspectorSubject";
import { post } from "./previewPost";
import { type TreeItem } from "./previewTree";
import { type PreviewViewport } from "./viewport";

/** What useInspector reads from the rest of the preview. */
export interface InspectorInputs {
    detailWantedRef: RefObject<string | null>;
    detailsRow: string | null;
    modelDetail: ModelDetail | null;
    setDetailsRow: Dispatch<SetStateAction<string | null>>;
    treeItems: TreeItem[];
    viewportRef: RefObject<PreviewViewport | null>;
}

export type Inspector = ReturnType<typeof useInspector>;

export function useInspector({
    detailWantedRef,
    detailsRow,
    modelDetail,
    setDetailsRow,
    treeItems,
    viewportRef,
}: InspectorInputs) {
    /**
     * What the row with its details open is made of.
     *
     * Depends on `treeItems` rather than only on the row id, because the map behind `inspectionOf`
     * is rebuilt by the same `treeItems()` call - so a level change or a reload refreshes the
     * flyout with the rows instead of leaving it describing geometry that is gone.
     */
    const inspectSources = useMemo(() => {
        if (
            detailsRow === null ||
            !treeItems.some((item) => item.id === detailsRow)
        ) {
            return [];
        }

        return viewportRef.current?.inspectionOf(detailsRow) ?? [];
    }, [detailsRow, treeItems]);

    /**
     * Everything the inspector tab needs to describe the row, or null when no row is being
     * inspected.
     *
     * Built here rather than there because only this side has the scene: `inspectionOf` reads the
     * loaded model, and the tab has no three.js of its own. It is the raw facts, not the rendered
     * panels - the tab runs the same `inspectPanels` over them.
     */
    const inspectorSubject = useMemo<InspectorSubject | null>(
        () =>
            detailsRow === null
                ? null
                : {
                      rowId: detailsRow,
                      sources: inspectSources,
                      modelDetail,
                      modelReference: detailWantedRef.current,
                  },
        [detailsRow, inspectSources, modelDetail],
    );

    /**
     * Keeps an open inspector on the row this preview is showing.
     *
     * `updateInspector` retargets a tab that is open and does nothing at all if none is - so
     * closing the tab keeps it closed, rather than any change here springing it open again.
     */
    useEffect(() => {
        if (inspectorSubject === null) {
            return;
        }

        post({
            type: "updateInspector",
            subject: inspectorSubject,
        });
    }, [inspectorSubject]);

    /**
     * Opens the inspector tab on one row.
     *
     * Carries the subject rather than leaving it to the effect above: pressing the button on the
     * row that is ALREADY being inspected changes no state, so the effect would not fire - and a
     * reader who closed the tab and pressed the same row again would get an empty one back.
     */
    const openInspector = useCallback(
        (rowId: string): void => {
            setDetailsRow(rowId);

            post({
                type: "openInspector",
                subject: {
                    rowId,
                    sources: viewportRef.current?.inspectionOf(rowId) ?? [],
                    modelDetail,
                    modelReference: detailWantedRef.current,
                } satisfies InspectorSubject,
            });
        },
        [modelDetail],
    );

    return {
        openInspector,
    };
}
