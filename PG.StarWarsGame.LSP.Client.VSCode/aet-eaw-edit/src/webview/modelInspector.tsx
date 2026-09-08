// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The item inspector as its own editor tab, opened beside the model preview.
//
// It was a 320px flyout pinned to the window, holding a mesh's sections AND its bulk geometry - ten
// columns of vertex data in a box a fifth the width of the screen. A tab rather than a takeover of
// the preview because the two are read TOGETHER: the answer to "why is this mesh black" is in the
// shader parameters here and in the model over there, and a page that hides the model to describe
// it makes the reader flip back and forth from memory.
//
// It computes nothing about the scene. The preview owns the three.js side and sends the facts; this
// runs the same `inspectPanels` over them and fetches its own geometry pages, which were always a
// server round trip.

import { useCallback, useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import styled from 'styled-components';

import { InspectorBody } from './inspector/InspectorBody';
import { GEOMETRY_PAGE, appendPage, type GeometryRows } from './preview/geometryRows';
import {
    geometryKeyOf, sameGeometryKey, type InspectorSubject,
} from './preview/inspectorSubject';
import { dockChromeCss, inspectorCss } from './shared/dockChrome';
import { type GeometryTable, type GetSubMeshGeometryResult } from '../protocol/modelPreview';

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };
const vscode = acquireVsCodeApi();

const Shell = styled.div`
    ${dockChromeCss}
    ${inspectorCss}

    height: 100%;
    overflow-y: auto;
    padding: var(--space-12) var(--space-16) var(--space-16);
    box-sizing: border-box;
    font-size: var(--font-size-12);
    color: var(--vscode-foreground);

    /* Tall, because a tab has the room a 320px flyout never did - but still BOUNDED. The table
       fetches its next page as the scroller nears the end, and a scroller with no height never
       nears anything: it would grow to fit every row and pull the whole sub-mesh down in one
       burst. Bounded, it fills the view and scrolls, which is what was asked for. */
    .geometry-scroll { max-height: 60vh; }

    .inspect-page-head {
        display: flex;
        align-items: baseline;
        gap: var(--space-8);
        flex-wrap: wrap;
        margin: 0 0 var(--space-12);
        padding-bottom: var(--space-8);
        border-bottom: var(--space-1) solid var(--vscode-panel-border, #444);
    }

    .inspect-page-title {
        margin: 0;
        font-size: var(--font-size-16);
        font-weight: 600;
        overflow-wrap: anywhere;
    }

    .inspect-page-subtitle {
        color: var(--vscode-descriptionForeground, #999);
        overflow-wrap: anywhere;
    }

    /* Columns that fill the width they are given rather than a fixed count: the sections are short
       and independent, so on a wide tab they sit side by side and on a narrow one they stack, with
       no breakpoint to keep in step with the tab's real width. */
    .inspect-columns {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
        gap: var(--space-16) var(--space-24);
        align-items: start;
    }

    /* The group margin is the column gap's job here - the flyout stacked these, so it spaced them
       with a top margin that would double the gap in a grid. */
    .inspect-columns .inspect-group + .inspect-group { margin-top: 0; }

    .inspect-geometry {
        margin-top: var(--space-16);
        padding-top: var(--space-12);
        border-top: var(--space-1) solid var(--vscode-panel-border, #444);
    }

    /* The panel is as wide as its widest table needs and no wider - a three-column bone mapping
       stretched across a full-width tab is mostly empty rule. It scrolls sideways within this. */
    .inspect-geometry .geometry-panel { max-width: 100%; }

    .inspect-empty {
        max-width: 46em;
        color: var(--vscode-descriptionForeground, #999);
    }
`;

function ModelInspector(): React.JSX.Element {
    const [subject, setSubject] = useState<InspectorSubject | null>(null);
    const [geometry, setGeometry] = useState<GeometryRows | null>(null);
    const [geometryError, setGeometryError] = useState<string | null>(null);

    // What has been ASKED for, so the same offset is never asked for twice. The scroller fires far
    // more often than a page comes back, and every one of those firings sees the same short table
    // until it does.
    const asked = useRef<string | null>(null);

    useEffect(() => {
        const handle = (event: MessageEvent): void => {
            const message = event.data as { type: string; [key: string]: unknown };

            if (message.type === 'setSubject') {
                setSubject(message.subject as InspectorSubject | null);
                return;
            }

            if (message.type === 'subMeshGeometry') {
                // Wrapped in `result` the way every other host reply is - the request either
                // answered or said why, and both halves travel together.
                const result = (message.result ?? {}) as GetSubMeshGeometryResult;

                asked.current = null;

                // Folded into what is already in hand rather than replacing it - the reader is
                // scrolling one long table, and the pages behind it are ours to keep track of.
                setGeometry(current =>
                    result.page === null || result.page === undefined
                        ? current
                        : appendPage(current, result.page));

                setGeometryError(result.error ?? null);
            }
        };

        window.addEventListener('message', handle);
        vscode.postMessage({ type: 'ready' });
        return () => window.removeEventListener('message', handle);
    }, []);

    // A page belongs to the sub-mesh it is OF, so it survives everything that does not change which
    // one is on screen. The subject is rebuilt whenever the preview's tree is - a visibility
    // checkbox is enough - and keying this on the subject's identity threw away a table the reader
    // was several pages into every time they touched the model.
    const key = subject === null ? null : geometryKeyOf(subject);
    const [shownKey, setShownKey] = useState(key);

    if (!sameGeometryKey(key, shownKey)) {
        setShownKey(key);
        setGeometry(null);
        setGeometryError(null);
        asked.current = null;
    }

    // `useCallback` because the scroller's effect depends on it: a fresh function every render
    // would re-run that effect every render, and it is the thing that asks for pages.
    const requestGeometry = useCallback((table: GeometryTable, offset: number): void => {
        if (key === null) {
            return;
        }

        // One request in flight, and never the same one twice. Every scroll event sees the same
        // short table until the page it asked for lands, so without this a single flick of the
        // wheel posts the same offset a dozen times.
        const wanted = `${table}:${offset}`;
        if (asked.current === wanted) {
            return;
        }

        asked.current = wanted;
        setGeometryError(null);
        vscode.postMessage({
            type: 'requestSubMeshGeometry',
            modelReference: key.modelReference,
            meshIndex: key.meshIndex,
            subMeshIndex: key.subMeshIndex,
            table,
            offset,
            count: GEOMETRY_PAGE,
        });
    }, [key?.modelReference, key?.meshIndex, key?.subMeshIndex]);

    // Switching tab starts that table again from row zero: the rows in hand belong to the table
    // they came from, and `appendPage` replaces rather than appends when the table changes.
    const openTable = useCallback((table: GeometryTable): void => {
        asked.current = null;
        requestGeometry(table, 0);
    }, [requestGeometry]);

    return (
        <Shell>
            <InspectorBody
                subject={subject}
                geometry={geometry}
                geometryError={geometryError}
                onGeometry={requestGeometry}
                onOpenTable={openTable}
            />
        </Shell>
    );
}

createRoot(document.getElementById('root')!).render(<ModelInspector />);
